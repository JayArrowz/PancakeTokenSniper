using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Util;
using System.Collections.Generic;
using System.Numerics;

namespace BscTokenSniper.Models
{
    public partial class SwapTokensForExactETHFunction : SwapTokensForExactETHFunctionBase { }

    [Function("swapTokensForExactETH", "uint256[]")]
    public class SwapTokensForExactETHFunctionBase : FunctionMessage
    {
        [Parameter("uint256", "amountOut", 1)]
        public virtual BigInteger AmountOut { get; set; }
        [Parameter("uint256", "amountInMax", 2)]
        public virtual BigInteger AmountInMax { get; set; }
        [Parameter("address[]", "path", 3)]
        public virtual List<string> Path { get; set; }
        [Parameter("address", "to", 4)]
        public virtual string To { get; set; }
        [Parameter("uint256", "deadline", 5)]
        public virtual BigInteger Deadline { get; set; }
    }

    public partial class SwapExactTokensForETHSupportingFeeOnTransferTokensFunction : SwapExactTokensForETHSupportingFeeOnTransferTokensFunctionBase { }

    [Function("swapExactTokensForETHSupportingFeeOnTransferTokens")]
    public class SwapExactTokensForETHSupportingFeeOnTransferTokensFunctionBase : FunctionMessage
    {
        [Parameter("uint256", "amountIn", 1)]
        public virtual BigInteger AmountIn { get; set; }
        [Parameter("uint256", "amountOutMin", 2)]
        public virtual BigInteger AmountOutMin { get; set; }
        [Parameter("address[]", "path", 3)]
        public virtual List<string> Path { get; set; }
        [Parameter("address", "to", 4)]
        public virtual string To { get; set; }
        [Parameter("uint256", "deadline", 5)]
        public virtual BigInteger Deadline { get; set; }
    }

}
