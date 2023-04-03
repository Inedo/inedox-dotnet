using System.Text;

namespace Inedo.Extensions.DotNet
{
    internal static class InternalExtensions
    {
        /// <summary>
        /// Safely appends an argument for use via CLI, and also appends a trailing space.
        /// </summary>
        /// <param name="sb">The <see cref="StringBuilder"/> instance.</param>
        /// <param name="arg">The argument to append and wrap with quotes.</param>
        public static void AppendArgument(this StringBuilder sb, string arg)
        {
            ArgumentNullException.ThrowIfNull(sb);

            if (string.IsNullOrEmpty(arg))
                return;

            sb.Append('\"');
            sb.Append(arg);
            if (arg.EndsWith('\\'))
                sb.Append('\\');
            sb.Append('\"');
            sb.Append(' ');
        }
    }
}
