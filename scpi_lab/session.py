"""仪器会话：打开/关闭、查询-响应配对、仪器错误队列检查。"""
from __future__ import annotations

from typing import Optional

from .command import parse_value


class ScpiError(RuntimeError):
    """仪器会话或仪器侧报告的错误。"""


class InstrumentSession:
    def __init__(self, transport, timeout_ms: int = 2000):
        if transport is None:
            raise ScpiError("缺少传输层")
        if timeout_ms <= 0:
            raise ScpiError("超时必须为正数")
        self._transport = transport
        self.timeout_ms = timeout_ms

    def open(self) -> None:
        self._transport.open()
        if not self._transport.is_open:
            raise ScpiError("会话打开失败")

    def close(self) -> None:
        self._transport.close()

    def __enter__(self) -> "InstrumentSession":
        self.open()
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.close()

    def write(self, command: str) -> None:
        self._ensure_open()
        self._transport.write(command)

    def query(self, command: str) -> str:
        """查询：命令必须以 '?' 结尾。用写命令去查询会拿到残留响应或阻塞，故提前失败。"""
        self._ensure_open()
        if command is None:
            raise ScpiError("命令为空")
        trimmed = command.strip()
        if not trimmed.endswith("?"):
            raise ScpiError("查询命令必须以 '?' 结尾：%s" % command)
        response = self._transport.write(trimmed)
        if response is None:
            raise ScpiError("查询无响应：%s" % command)
        return response

    def query_float(self, command: str) -> float:
        return parse_value(self.query(command))

    def identify(self) -> str:
        return self.query("*IDN?")

    def raise_if_instrument_error(self) -> None:
        """读取并判断仪器错误队列；有错误就抛，避免把仪器侧问题当成测试通过。"""
        raw = self.query("SYST:ERR?")
        if raw is None:
            return
        text = raw.strip()
        if text.startswith("0"):
            return
        raise ScpiError("仪器报告错误：%s" % text)

    def _ensure_open(self) -> None:
        if not self._transport.is_open:
            raise ScpiError("会话未打开")
