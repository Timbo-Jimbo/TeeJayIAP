using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace TimboJimbo.InAppPurchasing
{
    public delegate IPurchasingModule PurchasingModuleFactoryDel();
    public delegate void ConfigBuilderModifierDel(ConfigurationBuilder builder);
    
    public interface IInAppPurchaseProductReference
    {
        string GetReferencedProductId();
    }
    
    public static class InAppPurchasing
    {
        public static UnityEvent<Transaction> OnTransactionAdded => _transactions.OnAdded;
        public static UnityEvent<Transaction> OnTransactionChanged => _transactions.OnChanged;
        public static UnityEvent<Transaction> OnTransactionRemoved => _transactions.OnRemoved;
        public static IEnumerable<Transaction> ActiveTransactions => _transactions;
        public static IEnumerable<ProductDetails> AllProductDetails => _storeController?.products.all.Select(p => GetProductDetails(p.definition.id)) ?? Enumerable.Empty<ProductDetails>();
        public static InitializeState State { get; private set; } = InitializeState.NotInitialized;

        private static TransactionCollection _transactions = new();
        private static IStoreController _storeController;
        private static IExtensionProvider _extensions;

        public static async ValueTask<InitializeResult> Initialize(bool initUnityServices = true, IapLogLevel? logLevel = null, PurchasingModuleFactoryDel purchasingModuleFactory = null, ConfigBuilderModifierDel configBuilderModifier = null, CancellationToken ct = default)
        {
            L.LogLevel = logLevel ?? (Application.isEditor || Debug.isDebugBuild ? IapLogLevel.Verbose : IapLogLevel.Info);
            
            if(State == InitializeState.Initializing)
            {
                L.Warning("Already initializing. Waiting for completion...");
                await Utilities.WaitUntil(() => State != InitializeState.Initializing, ct);
                return State == InitializeState.Initialized ? InitializeResult.Success : InitializeResult.Failed;
                
            }
            else if(State == InitializeState.Initialized)
            {
                L.Warning($"Already initialized. Skipping initialization... Your {nameof(purchasingModuleFactory)} and {nameof(configBuilderModifier)} inputs will be ignored!");
                return InitializeResult.Success;
            }
            else if(State == InitializeState.InitializeFailed)
            {
                L.Warning("Purchasing initialization has been called before and has failed. Skipping initialization...");
                return InitializeResult.Failed;
            }

            try
            {
                L.Verbose("Initializing Purchasing...");
                State = InitializeState.Initializing;

                try
                {
                    await ValidateUnityServicesOrThrow(initUnityServices, ct);

                    IPurchasingModule purchasingModule = null;

                    try
                    {
                        purchasingModule = purchasingModuleFactory?.Invoke() ?? StandardPurchasingModule.Instance();
                        L.Verbose("Purchasing module created successfully!");
                    }
                    catch (Exception e)
                    {
                        throw new InAppPurchasingInitException("Failed to create Purchasing module!", e);
                    }

                    var builder = ConfigurationBuilder.Instance(purchasingModule);
                    L.Verbose("Configuration builder created successfully!");

                    try
                    {
                        configBuilderModifier?.Invoke(builder);
                        L.Verbose("Config builder modified successfully!");
                    }
                    catch (Exception e)
                    {
                        throw new InAppPurchasingInitException("Failed to modify configuration builder!", e);
                    }

                    var unityStoreListener = new UnityStoreListener();
                    var tcs = new TaskCompletionSource<InitializeResult>();
                    
                    {
                        unityStoreListener.InitializedUnityCallback += (controller, extensions) =>
                        {
                            L.Info("Purchasing initialized successfully!");
                            _storeController = controller;
                            _extensions = extensions;
                            tcs.TrySetResult(InitializeResult.Success);
                        };

                        unityStoreListener.InitializeFailedUnityCallback += (error, errorString) =>
                        {
                            L.Error($"Purchasing initialization failed! Error: {error}, Error String: {errorString}");
                            tcs.TrySetResult(InitializeResult.Failed);
                        };

                        unityStoreListener.ProcessPurchaseUnityCallback += purchaseEvent =>
                        {
                            L.Info($"Processing purchase: {purchaseEvent.purchasedProduct.definition.id}");
                            
                            ProcessNonFailurePurchaseUnityCallback(purchaseEvent.purchasedProduct);
                            return PurchaseProcessingResult.Pending;
                        };
                        
                        unityStoreListener.DeferredPurchaseUnityCallback += product =>
                        {
                            L.Info($"Deferred purchase detected! Product: {product.definition.id}");
                            ProcessNonFailurePurchaseUnityCallback(product);
                        };
                        
                        unityStoreListener.PurchaseFailedUnityCallback += (product, failureDescription) =>
                        {
                            L.Error($"Purchase failed! Product: {product.definition.id}, Failure Description: {failureDescription}");
                            ProcessFailureUnityCallback(product, failureDescription.reason);
                        };
                        
                        void ProcessFailureUnityCallback(Product product, PurchaseFailureReason unityFailureReason)
                        {
                            Utilities.InvokeDelayed(() =>
                            {
                                var transaction = _transactions.GetOrCreateTransaction(product);
                                
                                transaction.Set(
                                    state: TransactionState.Failed,
                                    failureReason: unityFailureReason switch
                                    {
                                        PurchaseFailureReason.PurchasingUnavailable => TransactionFailureReason.PurchasingUnavailable,
                                        PurchaseFailureReason.ExistingPurchasePending => TransactionFailureReason.ExistingPurchasePending,
                                        PurchaseFailureReason.ProductUnavailable => TransactionFailureReason.ProductUnavailable,
                                        PurchaseFailureReason.SignatureInvalid => TransactionFailureReason.SignatureInvalid,
                                        PurchaseFailureReason.UserCancelled => TransactionFailureReason.UserCancelled,
                                        PurchaseFailureReason.PaymentDeclined => TransactionFailureReason.PaymentDeclined,
                                        PurchaseFailureReason.DuplicateTransaction => TransactionFailureReason.DuplicateTransaction,
                                        PurchaseFailureReason.Unknown => TransactionFailureReason.Unknown,
                                        _ => TransactionFailureReason.Unknown
                                    }
                                );
                                
                                _transactions.RemoveTransaction(transaction);
                            });
                        }

                        void ProcessNonFailurePurchaseUnityCallback(Product product)
                        {
                            // We want to let the 'Pending' return result be processed by the IAP system before 
                            // we update the purchase state, since updating the purchase state could lead to
                            // immediately 'completing' the purchase by the end users code
                            Utilities.InvokeDelayed(() =>
                            {
                                var transaction = _transactions.GetOrCreateTransaction(product);
                                var state = TransactionState.ReadyToCollect;
                                var transactionId = product.transactionID;
                                var unifiedReceipt = JsonUtility.FromJson<UnifiedReceipt>(product.receipt);
                                var validationToken = Utilities.TryExtractValidationToken(unifiedReceipt);

                                var googlePlayStoreExtension = _extensions?.GetExtension<IGooglePlayStoreExtensions>();
                                if (googlePlayStoreExtension != null)
                                {
                                    if(googlePlayStoreExtension.IsPurchasedProductDeferred(product))
                                    {
                                        state = TransactionState.Deferred;
                                    }
                                }
                            
                                transaction.Set(state: state, receipt: unifiedReceipt, transactionId: transactionId, token: validationToken);
                            });
                        }
                        
                        unityStoreListener.ProcessConfigBuilder(builder);
                    }

                    L.Verbose("Products list: " + string.Join(", ", builder.products.Select(p => p.id)));
                    
                    L.Info("Initializing Purchasing...");
                    UnityPurchasing.Initialize(unityStoreListener, builder);
                    
                    return await tcs.Task.WithCancellationToken(ct);
                }
                finally
                {
                    State = InitializeState.Initialized;
                }
            }
            catch (Exception e)
            {
                State = InitializeState.InitializeFailed;

                if (e is InAppPurchasingInitException)
                {
                    L.Error(e.Message);
                    return InitializeResult.Failed;
                }
                else
                {
                    L.Exception("Failed to initialize purchasing", e);
                    State = InitializeState.InitializeFailed;
                    return InitializeResult.Failed;
                }
            }
            finally
            {
                L.Verbose("Purchasing initialization completed!");
            }
        }
        
        public static bool Available() => State == InitializeState.Initialized;

        public static Transaction GetTransaction(IInAppPurchaseProductReference iapProductRef) => GetTransaction(iapProductRef?.GetReferencedProductId() ?? string.Empty);
        public static Transaction GetTransaction(string productId) => _transactions.FirstOrDefault(t => t.Product.definition.id == productId);
        public static Transaction InitiateTransaction(IInAppPurchaseProductReference iapProductRef, bool completeAnyExistingTransaction = false) => InitiateTransaction(iapProductRef?.GetReferencedProductId() ?? string.Empty, completeAnyExistingTransaction);
        public static Transaction InitiateTransaction(string productId, bool completeAnyExistingTransaction = false)
        {
            EnsureInitializedOrThrow();
            
            var product = _storeController.products.WithID(productId);
            
            if (product == null)
                throw new ArgumentException($"Product with ID '{productId}' not found!");

            {
                var existingTransaction = _transactions.FirstOrDefault(p => p.Product.Equals(product));
                if (existingTransaction != null)
                {
                    if (!completeAnyExistingTransaction)
                    {
                        throw new ArgumentException($"Purchase for product {productId} already exists. Complete that one first.");
                    }
                    else
                    {
                        L.Warning($"Force-completing existing transaction to start a new one for product: {productId}");
                        CompleteTransaction(existingTransaction, wasConsumedAndAcknowledgedRemoted: false);
                    }
                }
            }
            
            var transaction = _transactions.CreateTransaction(product);

            if(State != InitializeState.Initialized)
            {
                L.Error("You need to make sure InAppPurchasing is initialized before calling InitiatePurchase!");
                transaction.Set(state: TransactionState.Failed);
                _transactions.RemoveTransaction(transaction);
                
                return transaction;
            }

            L.Info($"Initiating purchase for product: {productId}");
            _storeController.InitiatePurchase(productId);
            return transaction;
        }
        
        public static void CompleteTransaction(Transaction transaction, bool wasConsumedAndAcknowledgedRemoted)
        {
            EnsureInitializedOrThrow();
            
            if(transaction.State != TransactionState.ReadyToCollect && transaction.State != TransactionState.Deferred)
                throw new ArgumentException($"Transaction is not in a state to be completed! State: {transaction.State}");

            if (wasConsumedAndAcknowledgedRemoted)
            {
                L.Info($"Completing transaction for product: {transaction.Product.definition.id} (Was consumed and acknowledged remotely)");
                //we dont need to call ConfirmPendingPurchase here, as it was already consumed/acknowledged remotely..!
            }
            else
            {
                L.Info($"Completing transaction for product: {transaction.Product.definition.id}");
                try
                {
                    _storeController.ConfirmPendingPurchase(transaction.Product);
                }
                catch (Exception e)
                {
                    L.Exception($"Failed to confirm pending purchase for product: {transaction.Product.definition.id}", e);
                    transaction.Set(state: TransactionState.Failed, failureReason: TransactionFailureReason.Unknown);
                    _transactions.RemoveTransaction(transaction);
                    return;
                }
            }
            
            transaction.Set(state: TransactionState.Completed);
            _transactions.RemoveTransaction(transaction);
        }
        
        public static ProductDetails GetProductDetails(IInAppPurchaseProductReference iapProductRef) => GetProductDetails(iapProductRef?.GetReferencedProductId() ?? string.Empty);

        public static ProductDetails GetProductDetails(string productId)
        {
            if (!Available())
            {
                return new ProductDetails()
                {
                    Id = productId,
                    PurchaseAvailability = PurchaseAvailability.Unavailable(PurchaseUnavailableReason.Uninitialized)
                };
            }
            
            var unityProduct = _storeController.products.WithID(productId);
            var purchaseAvailability = DeterminePurchaseAvailability(unityProduct);
            var activeTransaction = _transactions.FirstOrDefault(t => t.Product.definition.id == productId);

            return new ProductDetails()
            {
                Id = productId,
                UnityProduct = unityProduct,
                ActiveTransaction = activeTransaction,
                PurchaseAvailability = purchaseAvailability
            };
                
            static PurchaseAvailability DeterminePurchaseAvailability([CanBeNull] Product product)
            {
                if (!Available()) PurchaseAvailability.Unavailable(PurchaseUnavailableReason.Uninitialized);
                
                if(product == null) return PurchaseAvailability.Unavailable(PurchaseUnavailableReason.UnknownProduct);
                if(!product.availableToPurchase) return PurchaseAvailability.Unavailable(PurchaseUnavailableReason.ProductUnavailable);
                
                var existingTransaction = _transactions.FirstOrDefault(t => t.Product.definition.id == product.definition.id);
                if (existingTransaction is { State: TransactionState.UserPerformingPurchase or TransactionState.Deferred or TransactionState.ReadyToCollect }) 
                    return PurchaseAvailability.Unavailable(PurchaseUnavailableReason.HasPendingPurchase);

                return PurchaseAvailability.Available();   
            }
        }
        
        private static void EnsureInitializedOrThrow()
        {
            if(State != InitializeState.Initialized)
                throw new InvalidOperationException("InAppPurchasing is not available! Make sure to call InAppPurchasing.Initialize() first if you haven't, otherwise check the console for Initialization errors.");
        }

        private static async ValueTask ValidateUnityServicesOrThrow(bool initUnityServices, CancellationToken ct = default)
        {
            L.Verbose("Ensuring Unity Services are initialized");
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                if (initUnityServices)
                {
                    L.Info("Unity Services are not initialized yet. Initialising and waiting for completion.");
                    await UnityServices.InitializeAsync().WithCancellationToken(ct);
                }
                else
                {
                    throw new InAppPurchasingInitException($"You need to make sure Unity Services are initialized before calling {nameof(InAppPurchasing)}.{nameof(Initialize)}! You can pass in `true` to {nameof(initUnityServices)} parameter to automatically initialize Unity Services if you prefer.");
                }
            }
            else if (UnityServices.State == ServicesInitializationState.Initializing)
            {
                L.Info("Unity Services are initializing. Waiting for completion...!");
                await Utilities.WaitUntil(() => UnityServices.State == ServicesInitializationState.Initialized, ct);
            }
                        
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                throw new InAppPurchasingInitException("Failed to initialize Unity Services!");
            }
        }
        
        private class UnityStoreListener : IDetailedStoreListener
        {
            public delegate void InitializeFailedUnityDel(InitializationFailureReason error, string errorString);
            public delegate PurchaseProcessingResult ProcessPurchaseUnityDel(PurchaseEventArgs purchaseEvent);
            public delegate void PurchaseFailedUnityDel(Product product, PurchaseFailureDescription failureDescription);
            public delegate void InitializedUnityDel(IStoreController controller, IExtensionProvider extensions);
            public delegate void DeferredPurchaseUnityDel(Product product);
            public event InitializedUnityDel InitializedUnityCallback;
            public event InitializeFailedUnityDel InitializeFailedUnityCallback;
            public event ProcessPurchaseUnityDel ProcessPurchaseUnityCallback;
            public event PurchaseFailedUnityDel PurchaseFailedUnityCallback;
            public event DeferredPurchaseUnityDel DeferredPurchaseUnityCallback;
            
            public void ProcessConfigBuilder(ConfigurationBuilder builder)
            {
                try
                {
                    var googlePlayConfiguration = builder.Configure<IGooglePlayConfiguration>();
                    
                    L.Verbose("Google Play configuration found. Setting deferred purchase listener...");
                    googlePlayConfiguration.SetDeferredPurchaseListener(product => DeferredPurchaseUnityCallback?.Invoke(product));
                    googlePlayConfiguration.SetFetchPurchasesExcludeDeferred(false);
                }
                catch (ArgumentException)
                {
                    // ignored - Google Play configuration is not supported on this platform, probably!
                }
            }
            
            public void OnInitializeFailed(InitializationFailureReason error, string errorString) => InitializeFailedUnityCallback?.Invoke(error, errorString);
            public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent) => ProcessPurchaseUnityCallback?.Invoke(purchaseEvent) ?? throw new NotImplementedException();
            public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription) => PurchaseFailedUnityCallback?.Invoke(product, failureDescription);
            public void OnInitialized(IStoreController controller, IExtensionProvider extensions) => InitializedUnityCallback?.Invoke(controller, extensions);
            
            // These are obsolete
            public void OnInitializeFailed(InitializationFailureReason error) => throw new NotImplementedException();
            public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason) => throw new NotImplementedException();
        }
        
        private class InAppPurchasingInitException : Exception
        {
            public InAppPurchasingInitException(string message, Exception inner = null) : base(message, inner) { }
        }
    }
}