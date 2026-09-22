"""可复用的测量脚本：电压扫描 → 采集 → 统计 → CSV 导出（对应"测试方法标准化、可复用"）。"""
from __future__ import annotations

import math
import statistics
from dataclasses import dataclass, field
from typing import List

from .instruments import PowerSupply
from .session import ScpiError


@dataclass
class SweepPoint:
    set_voltage: float
    measured_voltage: float
    measured_current: float


@dataclass
class SweepResult:
    points: List[SweepPoint] = field(default_factory=list)

    def mean_current(self) -> float:
        return statistics.fmean([p.measured_current for p in self.points]) if self.points else 0.0

    def std_current(self) -> float:
        if len(self.points) < 2:
            return 0.0
        return statistics.stdev([p.measured_current for p in self.points])

    def max_voltage_error(self) -> float:
        return max((abs(p.measured_voltage - p.set_voltage) for p in self.points), default=0.0)

    def to_csv(self) -> str:
        lines = ["set_voltage_V,measured_voltage_V,measured_current_A"]
        for p in self.points:
            lines.append("%.6g,%.6g,%.6g" % (p.set_voltage, p.measured_voltage, p.measured_current))
        return "\n".join(lines) + "\n"


class SweepRunner:
    """电压扫描：每个点设置后连读 settle_reads 次，取最后一次作为稳定读数。"""

    @staticmethod
    def run_voltage_sweep(psu: PowerSupply, start: float, stop: float, step: float,
                          settle_reads: int = 3) -> SweepResult:
        if step <= 0.0 or stop < start:
            raise ScpiError("扫描参数不合法：start=%s stop=%s step=%s" % (start, stop, step))
        result = SweepResult()
        count = int(math.floor((stop - start) / step + 1e-9)) + 1
        for i in range(count):
            volts = start + i * step
            psu.set_voltage(volts)
            measured_v = measured_i = 0.0
            for _ in range(max(1, settle_reads)):
                measured_v = psu.measure_voltage()
                measured_i = psu.measure_current()
            result.points.append(SweepPoint(volts, measured_v, measured_i))
        return result
