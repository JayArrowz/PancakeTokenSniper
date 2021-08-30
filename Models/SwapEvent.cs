using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BscTokenSniper.Models
{
    [Event("Swap")]
    public class SwapEvent : FunctionMessage
    {
        [Parameter("address", "sender", 1, true)]
        public virtual string Sender { get; set; }
        [Parameter("uint256", "amount0In", 2, false)]
        public virtual BigInteger Amount0In { get; set; }
        [Parameter("uint256", "amount1In", 3, false)]
        public virtual BigInteger Amount1In { get; set; }
        [Parameter("uint256", "amount0Out", 4, false)]
        public virtual BigInteger Amount0Out { get; set; }
        [Parameter("uint256", "amount1Out", 5, false)]
        public virtual BigInteger Amount1Out { get; set; }
        [Parameter("address", "to", 6, true)]
        public virtual string To { get; set; }
    }
}
