"""SCPI 命令解析：命令头（大小写不敏感）、查询后缀 '?'、逗号分隔多参数、数值+单位前缀。"""
from __future__ import annotations

import re
from typing import List

# 单位前缀 → 倍率（纯单位如 V/A/Hz 不缩放）
_PREFIX = {"k": 1e3, "K": 1e3, "M": 1e6, "G": 1e9,
           "m": 1e-3, "u": 1e-6, "U": 1e-6, "n": 1e-9, "p": 1e-12}

_NUMBER = re.compile(r"^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")


class ScpiParseError(ValueError):
    """SCPI 命令或参数格式错误。"""


def parse_value(raw: str) -> float:
    """把 "5V" / "10mA" / "1.5kHz" / "2e3" 解析成数值（SI 前缀按倍率换算）。"""
    if raw is None:
        raise ScpiParseError("参数为空")
    text = raw.strip()
    if not text:
        raise ScpiParseError("参数为空")
    match = _NUMBER.match(text)
    if not match:
        raise ScpiParseError("无法解析数值：%s" % raw)
    value = float(match.group(0))
    suffix = text[match.end():].strip()
    if suffix:
        value *= _PREFIX.get(suffix[0], 1.0)
    return value


class ScpiCommand:
    """一条 SCPI 命令：如 "MEAS:VOLT:DC?" 或 "SOUR:VOLT 5V"。"""

    def __init__(self, header: str, is_query: bool, params: List[str]):
        self.header = header.upper()
        self.is_query = is_query
        self.params = list(params)

    @classmethod
    def parse(cls, text: str) -> "ScpiCommand":
        if text is None:
            raise ScpiParseError("命令为空")
        trimmed = text.strip()
        if not trimmed:
            raise ScpiParseError("命令为空")
        head, _, rest = trimmed.partition(" ")
        is_query = head.endswith("?")
        if is_query:
            head = head[:-1]
        if not head:
            raise ScpiParseError("缺少命令头")
        params = [p.strip() for p in rest.split(",") if p.strip()] if rest.strip() else []
        return cls(head, is_query, params)

    def numeric(self, index: int) -> float:
        if index < 0 or index >= len(self.params):
            raise ScpiParseError("参数下标越界：%d" % index)
        return parse_value(self.params[index])

    def __str__(self) -> str:
        suffix = "?" if self.is_query else ""
        body = " ".join(self.params)
        return "%s%s%s" % (self.header, suffix, (" " + body) if body else "")
