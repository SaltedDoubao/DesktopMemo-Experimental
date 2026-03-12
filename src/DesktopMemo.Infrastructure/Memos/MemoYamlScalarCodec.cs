using System.Text;

namespace DesktopMemo.Infrastructure.Memos;

internal static class MemoYamlScalarCodec
{
    public static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        builder.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string Decode(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return trimmed;
        }

        var builder = new StringBuilder(trimmed.Length - 2);
        for (var i = 1; i < trimmed.Length - 1; i++)
        {
            var ch = trimmed[i];
            if (ch != '\\' || i == trimmed.Length - 2)
            {
                builder.Append(ch);
                continue;
            }

            i++;
            var esc = trimmed[i];
            switch (esc)
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                default:
                    builder.Append('\\');
                    builder.Append(esc);
                    break;
            }
        }

        return builder.ToString();
    }
}
