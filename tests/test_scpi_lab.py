"""自检：python -m unittest discover -s tests -v （仅用标准库，离线可跑）"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from scpi_lab import (DigitalMultimeter, InstrumentSession, MockInstrument, PowerSupply,
                      ScpiCommand, ScpiError, ScpiParseError, SweepRunner)
from scpi_lab.command import parse_value


class TestCommandParse(unittest.TestCase):
    def test_query_and_case(self):
        cmd = ScpiCommand.parse("meas:volt:dc?")
        self.assertEqual(cmd.header, "MEAS:VOLT:DC")
        self.assertTrue(cmd.is_query)
        self.assertEqual(str(cmd), "MEAS:VOLT:DC?")

    def test_multi_parameters(self):
        cmd = ScpiCommand.parse("SOUR:VOLT 5, 0.5")
        self.assertEqual(len(cmd.params), 2)
        self.assertAlmostEqual(cmd.numeric(1), 0.5)

    def test_unit_prefix(self):
        self.assertAlmostEqual(parse_value("5V"), 5.0)
        self.assertAlmostEqual(parse_value("10mA"), 0.01)
        self.assertAlmostEqual(parse_value("1.5kHz"), 1500.0)
        self.assertAlmostEqual(parse_value("2e3"), 2000.0)
        self.assertAlmostEqual(parse_value("-2.5e-3"), -0.0025)
        self.assertAlmostEqual(parse_value("3MOhm"), 3e6)

    def test_invalid(self):
        with self.assertRaises(ScpiParseError):
            parse_value("abc")
        with self.assertRaises(ScpiParseError):
            ScpiCommand.parse("   ")
        with self.assertRaises(ScpiParseError):
            ScpiCommand.parse("?")


class TestSession(unittest.TestCase):
    def test_query_requires_open(self):
        with self.assertRaises(ScpiError):
            InstrumentSession(MockInstrument()).query("*IDN?")

    def test_write_command_as_query_rejected(self):
        with InstrumentSession(MockInstrument()) as s:
            with self.assertRaises(ScpiError):
                s.query("SOUR:VOLT 5")
            self.assertTrue(s.identify().startswith("MockLab"))

    def test_instrument_error_queue(self):
        with InstrumentSession(MockInstrument()) as s:
            s.raise_if_instrument_error()          # 空队列不抛
            s.write("SOUR:VOLT 100")                # 超量程 → 仪器侧 -222
            with self.assertRaises(ScpiError):
                s.raise_if_instrument_error()

    def test_timeout_must_be_positive(self):
        with self.assertRaises(ScpiError):
            InstrumentSession(MockInstrument(), timeout_ms=0)


class TestInstruments(unittest.TestCase):
    def setUp(self):
        self.mock = MockInstrument()
        self.session = InstrumentSession(self.mock)
        self.session.open()
        self.psu = PowerSupply(self.session, max_voltage=30.0, max_current=3.0)

    def tearDown(self):
        self.session.close()

    def test_range_check_blocks_command(self):
        before = self.mock.command_count
        with self.assertRaises(ScpiError):
            self.psu.set_voltage(40.0)
        self.assertEqual(self.mock.command_count, before)      # 越界命令根本没下发
        with self.assertRaises(ScpiError):
            self.psu.set_current_limit(5.0)

    def test_set_and_readback(self):
        self.psu.set_voltage(12.0)
        self.assertAlmostEqual(self.mock.set_voltage, 12.0)
        self.assertAlmostEqual(self.psu.measure_voltage(), 5.0)

    def test_output_switch(self):
        self.psu.output(True)
        self.psu.output(False)
        self.session.raise_if_instrument_error()

    def test_dmm_range(self):
        dmm = DigitalMultimeter(self.session, max_voltage=1000.0)
        self.assertAlmostEqual(dmm.read_dc_voltage(), 5.0)
        tight = DigitalMultimeter(self.session, max_voltage=1.0)
        with self.assertRaises(ScpiError):
            tight.read_dc_voltage()


class TestSweep(unittest.TestCase):
    def setUp(self):
        self.mock = MockInstrument()
        self.session = InstrumentSession(self.mock)
        self.session.open()
        self.psu = PowerSupply(self.session)

    def tearDown(self):
        self.session.close()

    def test_point_count_and_last_point(self):
        result = SweepRunner.run_voltage_sweep(self.psu, 0.0, 10.0, 2.5)
        self.assertEqual(len(result.points), 5)
        self.assertAlmostEqual(result.points[-1].set_voltage, 10.0)

    def test_statistics_with_noise(self):
        clean = SweepRunner.run_voltage_sweep(self.psu, 0.0, 4.0, 1.0)
        self.assertEqual(clean.std_current(), 0.0)
        self.mock.set_noise(0.02)
        noisy = SweepRunner.run_voltage_sweep(self.psu, 0.0, 4.0, 1.0)
        self.assertGreater(noisy.std_current(), 0.0)
        self.assertGreater(noisy.max_voltage_error(), 0.0)
        self.assertGreater(noisy.mean_current(), 0.0)

    def test_csv_output(self):
        result = SweepRunner.run_voltage_sweep(self.psu, 0.0, 2.0, 1.0)
        csv = result.to_csv()
        self.assertTrue(csv.startswith("set_voltage_V"))
        self.assertEqual(len(csv.strip().split("\n")), 4)

    def test_invalid_sweep(self):
        with self.assertRaises(ScpiError):
            SweepRunner.run_voltage_sweep(self.psu, 10.0, 0.0, 1.0)
        with self.assertRaises(ScpiError):
            SweepRunner.run_voltage_sweep(self.psu, 0.0, 10.0, 0.0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
