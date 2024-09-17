using System.Linq;
using UnityEngine.Events;
using UnityEngine.Purchasing;

namespace TimboJimbo.InAppPurchasing
{
    public class Transaction
    {
        public UnityEvent<Transaction> OnChanged { get; } = new();
        public Product Product { get; private set; }
        public TransactionState State { get; private set; }
        public TransactionFailureReason FailureReason { get; private set; }
        public UnifiedReceipt Receipt { get; private set; }
        public string TransactionId { get; private set; }
        public InAppPurchaseToken Token { get; private set; }

        public Transaction(Product product)
        {
            Product = product ?? throw new System.ArgumentNullException(nameof(product));
        }

        internal void Set(TransactionState? state = null, TransactionFailureReason? failureReason = null, UnifiedReceipt receipt = null, string transactionId = null, InAppPurchaseToken? token = null)
        {
            var oldState = State;
            var oldFailureReason = FailureReason;
            var oldReceipt = Receipt;
            var oldTransactionId = TransactionId;
            var oldValidationToken = Token;
            
            bool stateChanged = false;
            if (state.HasValue)
            {
                stateChanged = oldState != state;
                State = state.Value;
            }
            
            bool failureReasonChanged = false;
            if (failureReason.HasValue)
            {
                failureReasonChanged = oldFailureReason != failureReason;
                FailureReason = failureReason.Value;
            }
            
            bool receiptChanged = false;
            if (receipt != null)
            {
                receiptChanged = oldReceipt != receipt;
                Receipt = receipt;
            }
            
            bool transactionIdChanged = false;
            if (transactionId != null)
            {
                transactionIdChanged = oldTransactionId != transactionId;
                TransactionId = transactionId;
            }
            
            bool validationTokenChanged = false;
            if (token.HasValue)
            {
                validationTokenChanged = oldValidationToken != token.Value;
                Token = token.Value;
            }

            if (stateChanged || failureReasonChanged || receiptChanged || transactionIdChanged || validationTokenChanged)
            {
                L.Verbose($"{(Product.metadata?.localizedTitle ?? Product.definition.id)} IAP Transaction updated: " +
                    string.Join(", ", new []
                    {
                        (stateChanged ? $"State: '{oldState}' -> '{State}'" : string.Empty),
                        (failureReasonChanged ? $"FailureReason: '{oldFailureReason}' -> '{FailureReason}'" : string.Empty),
                        (receiptChanged ? $"Receipt: '{oldReceipt}' -> '{Receipt}'" : string.Empty),
                        (transactionIdChanged ? $"TransactionID: '{oldTransactionId}' -> '{TransactionId}'" : string.Empty),
                        (validationTokenChanged ? $"ValidationToken: '{oldValidationToken}' -> '{Token}'" : string.Empty),
                    }.Where(s => !string.IsNullOrEmpty(s))));
                
                OnChanged.SafeInvoke(this);
            }
        }
    }
    
    public enum TransactionState
    {
        UserPerformingPurchase,
        Deferred,
        ReadyToCollect,
        Failed,
        Completed
    }

    public enum TransactionFailureReason
    {
        None,
        PurchasingUnavailable,
        ExistingPurchasePending,
        ProductUnavailable,
        SignatureInvalid,
        UserCancelled,
        PaymentDeclined,
        DuplicateTransaction,
        Unknown,
    }
}