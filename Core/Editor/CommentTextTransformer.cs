using System;
using System.Text;

namespace WFrameWork.Core.Editor
{
    /// <summary>Pure, idempotent source-header transformation used by the editor menu.</summary>
    public static class CommentTextTransformer
    {
        private const string Start = "/**************************************************";
        private const string End = "***************************************************/";

        public static string AddHeader(string text, string description = "Core")
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string bom = text.StartsWith("\uFEFF", StringComparison.Ordinal) ? "\uFEFF" : string.Empty;
            string body = bom.Length == 0 ? text : text.Substring(1);
            if (HasToolHeader(body)) return text;

            string header = string.Join(newline, new[]
            {
                Start, " *", " * Copyright (c) 2024 WangJian",
                " * Licensed under the MIT License. See LICENSE file in the project root for full license information.",
                " * author       : WangJian", " * create date  : 2024 11 05",
                " * description  : " + (description ?? string.Empty), " *", End
            });
            return bom + header + newline + body;
        }

        private static bool HasToolHeader(string body)
        {
            if (!body.StartsWith(Start, StringComparison.Ordinal)) return false;
            int lineEnd = body.IndexOf('\n');
            if (lineEnd < 0) return false;
            int end = body.IndexOf(End, lineEnd + 1, StringComparison.Ordinal);
            if (end < 0) return false;
            int blockEnd = end + End.Length;
            string block = body.Substring(0, blockEnd).Replace("\r\n", "\n");
            return block.IndexOf("Copyright (c) 2024 WangJian", StringComparison.Ordinal) >= 0 &&
                block.IndexOf("Licensed under the MIT License", StringComparison.Ordinal) >= 0 &&
                block.IndexOf("author       :", StringComparison.Ordinal) >= 0 &&
                block.IndexOf("create date  :", StringComparison.Ordinal) >= 0 &&
                block.IndexOf("description  :", StringComparison.Ordinal) >= 0;
        }

        public static byte[] TransformUtf8(byte[] bytes, string description = "Core")
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Encoding encoding = new UTF8Encoding(false, true);
            return TransformWithEncoding(bytes, encoding, hasBom ? 3 : 0, hasBom ? new byte[] { 0xEF, 0xBB, 0xBF } : null, description);
        }

        /// <summary>Detects a BOM and transforms only encodings that can be decoded reliably.</summary>
        public static byte[] TransformSource(byte[] bytes, string description = "Core")
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return TransformWithEncoding(bytes, new UTF8Encoding(false, true), 3, new byte[] { 0xEF, 0xBB, 0xBF }, description);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return TransformWithEncoding(bytes, new UnicodeEncoding(false, false, true), 2, new byte[] { 0xFF, 0xFE }, description);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return TransformWithEncoding(bytes, new UnicodeEncoding(true, false, true), 2, new byte[] { 0xFE, 0xFF }, description);
            return TransformWithEncoding(bytes, new UTF8Encoding(false, true), 0, null, description);
        }

        private static byte[] TransformWithEncoding(byte[] bytes, Encoding encoding, int offset, byte[] preamble, string description)
        {
            string text = encoding.GetString(bytes, offset, bytes.Length - offset);
            string transformed = AddHeader(text, description);
            byte[] body = encoding.GetBytes(transformed.StartsWith("\uFEFF", StringComparison.Ordinal) ? transformed.Substring(1) : transformed);
            if (preamble == null) return body;
            var result = new byte[body.Length + preamble.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            return result;
        }
    }
}
