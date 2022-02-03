namespace BscTokenSniper
{
    internal class HoneyPotResponse
    {

        public bool NoLiquidity { get; set; }
        public bool IsHoneypot { get; set; }
        public object Error { get; set; }
        public int MaxTxAmount { get; set; }
        public int MaxTxAmountBNB { get; set; }
        public float BuyTax { get; set; }
        public float SellTax { get; set; }
        public int BuyGas { get; set; }
        public int SellGas { get; set; }
    }
}