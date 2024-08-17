namespace TimboJimbo.InAppPurchasing
{
    public struct PurchaseAvailability
    {
        public bool IsAvailable;
        public PurchaseUnavailableReason UnavailableReason;
        
        public static PurchaseAvailability Available()
        {
            return new PurchaseAvailability
            {
                IsAvailable = true,
                UnavailableReason = PurchaseUnavailableReason.None
            };
        }
        
        public static PurchaseAvailability Unavailable(PurchaseUnavailableReason reason)
        {
            return new PurchaseAvailability
            {
                IsAvailable = false,
                UnavailableReason = reason
            };
        }
    }
}