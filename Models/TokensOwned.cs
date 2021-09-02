using Fractions;
using Nethereum.Util;
using System.Numerics;

namespace BscTokenSniper.Models
{
    public class TokensOwned
    {
        public string Address { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger BnbAmount { get; set; }
        public Fraction SinglePrice { get; set; }
        public int TokenIdx { get; set; }
        public string PairAddress { get; set; }
        public int Decimals { get; set; }
        public bool HoneypotCheck { get; set; }
        public bool FailedSell { get; set; }
    }
}
