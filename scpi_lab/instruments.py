"""设备类封装：把 SCPI 细节收在类里，脚本只调用语义化方法，并在上层做量程检查。"""
from __future__ import annotations

from .session import InstrumentSession, ScpiError


class PowerSupply:
    def __init__(self, session: InstrumentSession, max_voltage: float = 30.0, max_current: float = 3.0):
        if session is None:
            raise ScpiError("缺少会话")
        self._session = session
        self.max_voltage = max_voltage
        self.max_current = max_current

    def set_voltage(self, volts: float) -> None:
        if volts < 0.0 or volts > self.max_voltage:
            raise ScpiError("电压 %s V 超出量程 0~%s V" % (volts, self.max_voltage))
        self._session.write("SOUR:VOLT %s" % volts)
        self._session.raise_if_instrument_error()

    def set_current_limit(self, amps: float) -> None:
        if amps <= 0.0 or amps > self.max_current:
            raise ScpiError("限流 %s A 超出量程 0~%s A" % (amps, self.max_current))
        self._session.write("SOUR:CURR %s" % amps)
        self._session.raise_if_instrument_error()

    def output(self, on: bool) -> None:
        self._session.write("OUTP %s" % ("ON" if on else "OFF"))
        self._session.raise_if_instrument_error()

    def measure_voltage(self) -> float:
        return self._session.query_float("MEAS:VOLT:DC?")

    def measure_current(self) -> float:
        return self._session.query_float("MEAS:CURR:DC?")


class DigitalMultimeter:
    def __init__(self, session: InstrumentSession, max_voltage: float = 1000.0):
        self._session = session
        self.max_voltage = max_voltage

    def read_dc_voltage(self) -> float:
        value = self._session.query_float("MEAS:VOLT:DC?")
        if abs(value) > self.max_voltage:
            raise ScpiError("读数 %s V 超出量程 %s V" % (value, self.max_voltage))
        return value

    def read_dc_current(self) -> float:
        return self._session.query_float("MEAS:CURR:DC?")
