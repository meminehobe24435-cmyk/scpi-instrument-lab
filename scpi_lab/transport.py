"""仪器传输层：真实实现可以是串口 / TCP / USBTMC（PyVISA），这里提供可复现的模拟仪器。"""
from __future__ import annotations

from .command import ScpiCommand, ScpiParseError


class MockInstrument:
    """模拟台式电源 + DMM 的少量 SCPI 子系统，用于无真机回归测试。

    刻意保留"仪器不报错但静默截断"之外的常见行为：越界参数进错误队列（SYST:ERR?），
    这样上层必须读错误队列才能发现问题。
    """

    IDN = "MockLab,PSU-1000,SN0001,1.0.0"

    def __init__(self, idn: str = None):
        if idn:
            self.IDN = idn
        self.is_open = False
        self.command_count = 0
        self._errors = []
        self._set_voltage = 0.0
        self._set_current = 1.0
        self._measured_voltage = 5.0
        self._measured_current = 0.1
        self._frequency = 1000.0
        self._noise = 0.0
        self._noise_state = 12345

    # ---- 会话控制 ----
    def open(self) -> None:
        self.is_open = True

    def close(self) -> None:
        self.is_open = False

    # ---- 测试辅助（模拟真机行为，便于验证统计与量程逻辑）----
    def set_noise(self, noise: float) -> None:
        self._noise = noise

    def set_measured(self, voltage: float = None, current: float = None) -> None:
        if voltage is not None:
            self._measured_voltage = voltage
        if current is not None:
            self._measured_current = current

    @property
    def set_voltage(self) -> float:
        return self._set_voltage

    # ---- 传输 ----
    def write(self, command: str):
        if not self.is_open:
            raise RuntimeError("会话未打开")
        self.command_count += 1
        cmd = ScpiCommand.parse(command)
        head = cmd.header

        if head == "*IDN":
            return self.IDN
        if head == "*RST":
            self._set_voltage = 0.0
            self._set_current = 1.0
            self._errors.clear()
            return None
        if head in ("SYST:ERR", "SYSTEM:ERROR"):
            if not self._errors:
                return '0,"No error"'
            return self._errors.pop(0)
        if head in ("SOUR:VOLT", "SOURCE:VOLTAGE"):
            if len(cmd.params) != 1:
                self._errors.append('-109,"Missing parameter"')
                return None
            try:
                volts = cmd.numeric(0)
            except ScpiParseError:
                self._errors.append('-104,"Data type error"')
                return None
            if volts < 0.0 or volts > 30.0:
                self._errors.append('-222,"Data out of range"')
                return None
            self._set_voltage = volts
            return None
        if head in ("SOUR:CURR", "SOURCE:CURRENT"):
            if len(cmd.params) != 1:
                self._errors.append('-109,"Missing parameter"')
                return None
            self._set_current = cmd.numeric(0)
            return None
        if head in ("MEAS:VOLT:DC", "MEASURE:VOLTAGE:DC"):
            return self._fmt(self._measured_voltage + self._noise_now())
        if head in ("MEAS:CURR:DC", "MEASURE:CURRENT:DC"):
            return self._fmt(self._measured_current + self._noise_now())
        if head in ("SENS:FREQ", "SENSE:FREQUENCY"):
            if len(cmd.params) == 1:
                self._frequency = cmd.numeric(0)
                return None
            return self._fmt(self._frequency)
        if head in ("OUTP", "OUTPUT"):
            if len(cmd.params) == 1:
                value = cmd.params[0].upper()
                if value not in ("ON", "OFF", "1", "0"):
                    self._errors.append('-224,"Illegal parameter value"')
            return None
        self._errors.append('-113,"Undefined header"')
        return None

    def _noise_now(self) -> float:
        """确定性伪随机抖动（LCG）：固定偏移没有标准差；"奇偶交替"也不行——每个扫描点
        发出的命令数是偶数，末次读数的符号会完全一致。用可复现的伪随机序列才既能产生抖动、
        又能让测试稳定复现。"""
        if self._noise == 0.0:
            return 0.0
        self._noise_state = (self._noise_state * 1103515245 + 12345) % (2 ** 31)
        frac = (self._noise_state / float(2 ** 31)) * 2.0 - 1.0
        return self._noise * frac

    @staticmethod
    def _fmt(value: float) -> str:
        return ("%.6f" % value).rstrip("0").rstrip(".") or "0"
