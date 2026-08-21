using System.Text;

namespace AncestorsEnhanced.Infrastructure.Parsing;

internal static class ValveKeyValueParser
{
    public static ValveKeyValueObject Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        TokenReader reader = new(content);
        return ParseObject(reader, requiresClosingBrace: false);
    }

    private const int MaximumDepth = 64;
    private const int MaximumTokens = 100_000;
    private const int MaximumTokenLength = 65_536;
    private const int MaximumTokenCharacters = 4 * 1024 * 1024;

    private static ValveKeyValueObject ParseObject(TokenReader reader, bool requiresClosingBrace) => ParseObject(reader, requiresClosingBrace, depth: 0);

    private static ValveKeyValueObject ParseObject(TokenReader reader, bool requiresClosingBrace, int depth)
    {
        if (depth >= MaximumDepth)
        {
            throw new FormatException("Valve KeyValues nesting is too deep.");
        }

        ValveKeyValueObject result = new();

        while (reader.TryRead(out Token token))
        {
            if (token.Kind == TokenKind.CloseBrace)
            {
                if (!requiresClosingBrace)
                {
                    throw new FormatException("Unexpected closing brace in Valve KeyValues data.");
                }

                return result;
            }

            if (token.Kind is TokenKind.OpenBrace)
            {
                throw new FormatException("A Valve KeyValues entry must begin with a key.");
            }

            string key = token.Value;
            if (!reader.TryRead(out Token valueToken))
            {
                throw new FormatException($"Missing value for Valve KeyValues key '{key}'.");
            }

            if (valueToken.Kind == TokenKind.OpenBrace)
            {
                result.Add(new ValveKeyValueEntry(
                    key,
                    null,
                    ParseObject(reader, requiresClosingBrace: true, depth: depth + 1)));
                continue;
            }

            if (valueToken.Kind == TokenKind.CloseBrace)
            {
                throw new FormatException($"Missing value for Valve KeyValues key '{key}'.");
            }

            result.Add(new ValveKeyValueEntry(key, valueToken.Value, null));
        }

        if (requiresClosingBrace)
        {
            throw new FormatException("Valve KeyValues object is missing a closing brace.");
        }

        return result;
    }

    private enum TokenKind
    {
        Text,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    private sealed class TokenReader(string content)
    {
        private int _position;
        private int _tokenCount;
        private int _tokenCharacters;

        public bool TryRead(out Token token)
        {
            SkipWhitespaceAndComments();
            if (_position >= content.Length)
            {
                token = default;
                return false;
            }
            if (++_tokenCount > MaximumTokens)
            {
                throw new FormatException("Valve KeyValues contains too many tokens.");
            }

            char current = content[_position];
            if (current == '{' || current == '}')
            {
                _position++;
                token = new Token(
                    current == '{' ? TokenKind.OpenBrace : TokenKind.CloseBrace,
                    current.ToString());
                return true;
            }

            string value = current == '"' ? ReadQuotedText() : ReadBareText();
            if (value.Length > MaximumTokenLength ||
                value.Length > MaximumTokenCharacters - _tokenCharacters)
            {
                throw new FormatException("Valve KeyValues contains too much text.");
            }
            _tokenCharacters += value.Length;
            token = new Token(TokenKind.Text, value);
            return true;
        }

        private string ReadQuotedText()
        {
            _position++;
            StringBuilder value = new();

            while (_position < content.Length)
            {
                char current = content[_position++];
                if (current == '"')
                {
                    return value.ToString();
                }

                if (current == '\\' && _position < content.Length)
                {
                    char escaped = content[_position++];
                    if (escaped is '\\' or '"')
                    {
                        value.Append(escaped);
                    }
                    else
                    {
                        value.Append('\\').Append(escaped);
                    }

                    continue;
                }

                value.Append(current);
            }

            throw new FormatException("Unterminated quoted string in Valve KeyValues data.");
        }

        private string ReadBareText()
        {
            int start = _position;
            while (_position < content.Length)
            {
                char current = content[_position];
                if (char.IsWhiteSpace(current) || current is '{' or '}')
                {
                    break;
                }

                _position++;
            }

            return content[start.._position];
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < content.Length)
            {
                if (char.IsWhiteSpace(content[_position]))
                {
                    _position++;
                    continue;
                }

                if (_position + 1 < content.Length &&
                    content[_position] == '/' &&
                    content[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < content.Length && content[_position] is not '\r' and not '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                return;
            }
        }
    }
}
