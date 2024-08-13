using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;
using UnityEngine.Purchasing;

namespace TimboJimbo.InAppPurchasing
{
    internal class TransactionCollection : IEnumerable<Transaction>
    {
        public UnityEvent<Transaction> OnAdded { get; } = new();
        public UnityEvent<Transaction> OnChanged { get; } = new();
        public UnityEvent<Transaction> OnRemoved { get; } = new();
        
        private readonly List<Transaction> _transactions = new();
        public TransactionCollection()
        {
            OnAdded.AddListener(transaction =>
            {
                L.Verbose($"Transaction was added: {transaction}");
                transaction.OnChanged.AddListener(_ =>
                {
                    OnChanged.SafeInvoke(transaction);
                });
            });
            
            OnRemoved.AddListener(transaction =>
            {
                L.Verbose($"Transaction was removed: {transaction}");
            });
        }
        
        public Transaction CreateTransaction(Product product)
        {
            if(_transactions.Any(p => p.Product.Equals(product)))
                throw new Exception($"Transaction for product {product.definition.id} already exists. Complete it first.");
            
            var transaction = new Transaction(product);
            _transactions.Add(transaction);
            OnAdded.SafeInvoke(transaction);
            return transaction;
        }
        
        public Transaction GetOrCreateTransaction(Product product)
        {
            return _transactions.FirstOrDefault(p => p.Product.Equals(product)) ?? CreateTransaction(product);
        }
        
        public void RemoveTransaction(Transaction transaction)
        {
            _transactions.Remove(transaction);
            OnRemoved.SafeInvoke(transaction);
        }

        public IEnumerator<Transaction> GetEnumerator()
        {
            return _transactions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}