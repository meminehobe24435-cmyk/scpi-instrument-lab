using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScpiLab
{
    /// <summary>SCPI 命令的通用表示：头（大小写不敏感）+ 参数表 + 是否查询。</summary>
    public sealed class ScpiCommand
    {
        public string Header { get; private set; }
        public IReadOnlyList<string> Parameters { get { return _params; } }
        public bool IsQuery { get; private set; }

        private readonly List<string> _params = new List<string>();

        private ScpiCommand(string header, bool isQuery)
        {
            Header = header.ToUpperInvariant();
            IsQuery = isQuery;
        }

        /// <summary>
        /// 解析一条 SCPI 命令，例如 "MEAS:VOLT:DC? 10,0.001" 或 "SOUR:VOLT 5V"。
        /// 支持：短/长格式、查询后缀 '?'、逗号分隔多参数、空格分隔、分号多命令由上层拆分。
        /// </summary>
        public static ScpiCommand Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var trimmed = text.Trim();
            if (trimmed.Length == 0) throw new FormatException("空命令");

            var sep = trimmed.IndexOf(' ');
            string head = sep < 0 ? trimmed : trimmed.Substring(0, sep);
            string rest = sep < 0 ? string.Empty : trimmed.Substring(sep + 1).Trim();

            bool isQuery = head.EndsWith("?", StringComparison.Ordinal);
            if (isQuery) head = head.Substring(0, head.Length - 1);
            if (head.Length == 0) throw new FormatException("缺少命令头");

            var cmd = new ScpiCommand(head, isQuery);
            if (rest.Length > 0)
            {
                foreach (var part in rest.Split(','))
                {
                    var p = part.Trim();
                    if (p.Length > 0) cmd._params.Add(p);
                }
            }
            return cmd;
        }

        /// <summary>按数值解析第 index 个参数（支持 5V / 10mA / 1.5kHz / 2e3 等写法）。</summary>
        public double NumericParameter(int index)
        {
            if (index < 0 || index >= _params.Count) throw new ArgumentOutOfRangeException("index");
            return ParseValue(_params[index]);
        }

        public int ParameterCount { get { return _params.Count; } }

        /// <summary>数值 + 单位后缀解析：k/M/m/u/n 前缀，V/A/Hz/s/Ohm 等单位一律忽略。</summary>
        public static double ParseValue(string raw)
        {
            if (raw == null) throw new ArgumentNullException("raw");
            var s = raw.Trim();
            if (s.Length == 0) throw new FormatException("空参数");

            int i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            var numberPart = s.Substring(0, i);
            var suffix = s.Substring(i).Trim();
            if (numberPart.Length == 0 || numberPart == "+" || numberPart == "-")
                throw new FormatException("无法解析数值：" + raw);

            double value = double.Parse(numberPart, CultureInfo.InvariantCulture);
            double scale = 1.0;
            if (suffix.Length > 0)
            {
                switch (suffix[0])
                {
                    case 'k': case 'K': scale = 1e3; break;
                    case 'M': scale = 1e6; break;
                    case 'G': scale = 1e9; break;
                    case 'm': scale = 1e-3; break;
                    case 'u': case 'U': scale = 1e-6; break;
                    case 'n': scale = 1e-9; break;
                    case 'p': scale = 1e-12; break;
                    default: scale = 1.0; break;      /* 纯单位（V/A/Hz）不缩放 */
                }
            }
            return value * scale;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Header);
            if (IsQuery) sb.Append('?');
            if (_params.Count > 0) sb.Append(' ').Append(string.Join(",", _params));
            return sb.ToString();
        }
    }
}
