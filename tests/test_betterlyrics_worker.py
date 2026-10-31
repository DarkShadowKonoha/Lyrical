import importlib.util
import json
import unittest
from pathlib import Path

WORKER = Path(__file__).parents[1] / "providers" / "betterlyrics" / "worker.py"
spec = importlib.util.spec_from_file_location("betterlyrics_worker", WORKER)
worker = importlib.util.module_from_spec(spec)
assert spec and spec.loader
spec.loader.exec_module(worker)


class QrcConversionTests(unittest.TestCase):
    def test_converts_qrc_word_offsets_to_enhanced_lrc(self):
        qrc = (
            "[0,2000]Hello(0,400) world(500,500)\n"
            "[2500,1500]again(2500,600) now(3200,400)"
        )
        lrc = worker.qrc_to_enhanced_lrc(qrc)
        self.assertEqual(
            lrc,
            "[00:00.00]<00:00.00>Hello<00:00.50> world\n"
            "[00:02.50]<00:02.50>again<00:03.20> now",
        )

    def test_extracts_qrc_from_betterlyrics_xml_wrapper(self):
        payload = {
            "lyrics": '<?xml version="1.0"?><QrcInfos><LyricInfo '
            'LyricContent="[0,2000]Hello(0,400) world(500,500)\n'
            '[2500,1500]again(2500,600) now(3200,400)"/></QrcInfos>'
        }
        qrc = worker.extract_qrc(payload)
        self.assertIsNotNone(qrc)
        self.assertTrue(qrc.startswith("[0,2000]"), qrc)
        self.assertIsNotNone(worker.qrc_to_enhanced_lrc(qrc))

    def test_direct_qrc_payload_is_supported(self):
        self.assertEqual(worker.extract_qrc("[0,100]hi(0,50)"), "[0,100]hi(0,50)")

    def test_rejects_untimed_or_single_chunk_data(self):
        self.assertIsNone(worker.qrc_to_enhanced_lrc("[0,1000]plain text"))
        self.assertIsNone(worker.qrc_to_enhanced_lrc("[0,1000]only(0,500)"))

    def test_timestamp_handles_minutes(self):
        self.assertEqual(worker.timestamp(61_230), "01:01.23")


if __name__ == "__main__":
    unittest.main()
