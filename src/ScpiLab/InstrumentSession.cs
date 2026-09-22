using System;

namespace ScpiLab
{
    public sealed class ScpiException : Exception
    {
        public ScpiException(string message) : base(message) { }
    }

    /// <summary>
    /// 仪器会话：负责打开/关闭、查询-响应配对、超时与错误队列检查。
    /// 上层只看到"写命令 / 读响应"，不关心下面是串口、TCP 还是模拟器。
    /// </summary>
    public sealed class InstrumentSession : IDisposable
    {
        private readonly ITransport _transport;
        private int _timeoutMs;

        public InstrumentSession(ITransport transport, int timeoutMs = 2000)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            _transport = transport;
            _timeoutMs = timeoutMs;
        }

        public int TimeoutMs
        {
            get { return _timeoutMs; }
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException("value");
                _timeoutMs = value;
            }
        }

        public void Open()
        {
            _transport.Open();
            if (!_transport.IsOpen) throw new ScpiException("会话打开失败");
        }

        public void Close() { _transport.Close(); }

        /// <summary>写命令（不需要响应）。</summary>
        public void Write(string command)
        {
            EnsureOpen();
            _transport.Write(command);
        }

        /// <summary>查询：命令必须以 '?' 结尾，否则视为编程错误（提前失败优于拿到错数据）。</summary>
        public string Query(string command)
        {
            EnsureOpen();
            if (command == null) throw new ArgumentNullException("command");
            var trimmed = command.Trim();
            if (!trimmed.EndsWith("?", StringComparison.Ordinal))
                throw new ScpiException("查询命令必须以 '?' 结尾：" + command);
            var response = _transport.Write(trimmed);
            if (response == null) throw new ScpiException("查询无响应：" + command);
            return response;
        }

        public double QueryDouble(string command)
        {
            var raw = Query(command);
            return ScpiCommand.ParseValue(raw);
        }

        public string Identify() { return Query("*IDN?"); }

        /// <summary>读取并判断仪器错误队列；非 "0,No error" 抛异常（把仪器侧错误暴露出来而不是吞掉）。</summary>
        public void ThrowIfInstrumentError()
        {
            var err = Query("SYST:ERR?");
            if (err == null) return;
            var t = err.Trim();
            if (t.StartsWith("0", StringComparison.Ordinal)) return;
            throw new ScpiException("仪器报告错误：" + t);
        }

        private void EnsureOpen()
        {
            if (!_transport.IsOpen) throw new ScpiException("会话未打开");
        }

        public void Dispose() { _transport.Close(); }
    }
}
