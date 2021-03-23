using System;
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
            if (sb == null)
                throw new ArgumentNullException(nameof(sb));

            bool needsQuotes = false;
            if (!string.IsNullOrEmpty(arg))
            {
                foreach (char c in arg)
                {
                    if (char.IsWhiteSpace(c) || c == '\"')
                    {
                        needsQuotes = true;
                        break;
                    }
                }
            }

            if (needsQuotes)
            {
                sb.Append('\"');
                sb.Append(arg);
                // escape any quote characters after the first one
                sb.Replace("\"", "\\\"", 1, sb.Length - 1);

                // if last character is a \, add another one to prevent it from escaping the closing quote
                if (sb[sb.Length - 1] == '\\')
                    sb.Append('\\');

                sb.Append('\"');
            }
            else
            {
                sb.Append(arg);
            }

            sb.Append(' ');
        }
    }
}
