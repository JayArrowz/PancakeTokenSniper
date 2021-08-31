using Nethereum.Util;
using System.Numerics;

namespace BscTokenSniper.Models
{
    public class TokensOwned
    {
        public string Address { get; set; }
        public BigInteger Amount { get; set; }
        public double SinglePrice { get; set; }
        public int TokenIdx { get; set; }
        public string PairAddress { get; set; }
        public int Decimals { get; set; }
    }
}
