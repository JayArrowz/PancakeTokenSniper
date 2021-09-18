using System;
using System.Numerics;

namespace BscTokenSniper.Models
{
    public class SniperConfiguration
    {
        public string V1PancakeswapRouterAddress { get; set; }
        public string[] ContractRugCheckStrings { get; set; }
        public string WalletAddress { get; set; }
        public string WalletPrivateKey { get; set; }
        public string PancakeswapRouterAddress { get; set; }
        public string PancakeswapFactoryAddress { get; set; }
        public double AmountToSnipe { get; set; }
        public int TransactionRevertTimeSeconds { get; set; }
        public long GasAmount { get; set; }
        public string BscHttpApi { get; set; }
        public string BscNode { get; set; }
        public string LiquidityPairAddress { get; set; }
        public string BscScanApikey { get; set; }
        public bool RugCheckEnabled { get; set; }
        public double MinLiquidityAmount { get; set; }
        public int ChainId { get; set; }
        public double ProfitPercentageMargin { get; set; }
        public bool RenounceOwnershipCheck { get; set; }
        public decimal MinimumPercentageOfTokenInLiquidityPool { get; set; }
        public bool HoneypotCheck { get; set; }
        public double HoneypotCheckAmount { get; set; }
        public double SellSlippage { get; set; }
        public bool RugdocCheckEnabled { get; set; }
        public string[] DeadWallets { get; set; }
        public string[] WhitelistedTokens { get; set; }
        public int BuyDelaySeconds { get; set; }
        public bool CheckRouterAddressInContract { get; set; }
        public bool OnlyBuyWhitelist { get; set; }
    }
}
