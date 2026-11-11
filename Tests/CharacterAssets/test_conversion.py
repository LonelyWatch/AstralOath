import sys, unittest, struct, tempfile
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools" / "CharacterAssets"))
import convert

class ConversionTests(unittest.TestCase):
    def test_rejects_truncated_scsp(self):
        with self.assertRaises(ValueError): convert.decode_scsp(b"bad")
    def test_rejects_unknown_sct(self):
        with self.assertRaises(ValueError): convert.decode_sct(b"SCT" + bytes(30))
    def test_rgb565_alpha(self):
        import lz4.block
        raw = struct.pack("<HH", 0xf800, 0x07e0) + bytes([255,128])
        packed = lz4.block.compress(raw,store_size=False)
        data=b"SCT"+struct.pack("<BBHHII",1,4,2,1,len(raw),len(packed))+packed
        image, report = convert.decode_sct(data)
        self.assertEqual(list(image.getdata()),[(255,0,0,255),(0,255,0,128)])
        self.assertEqual(report["width"],2)
    def test_sct_format2_rgba(self):
        import lz4.block
        raw=bytes([0,0,0,0,128,128,128,128])
        packed=lz4.block.compress(raw,store_size=False)
        data=b"SCT"+struct.pack("<BBHHII",1,2,2,1,len(raw),len(packed))+packed
        image,report=convert.decode_sct(data)
        self.assertEqual(list(image.getdata()),[(0,0,0,0),(128,128,128,128)])
    def test_scsp_outer_lengths(self):
        import lz4.block
        raw=struct.pack("<II",2,1)+b"abx"
        packed=lz4.block.compress(raw,store_size=False)
        data=struct.pack("<II",len(raw),len(packed))+packed
        with self.assertRaises(ValueError): convert.decode_scsp(data+b"extra")


def fixture_v2(timeline=None):
    import lz4.block
    strings=["hash","2.1.27.scsp","root","ping","idle"]
    pool=b""; offsets={}
    for value in strings: offsets[value]=len(pool);pool+=value.encode()+b"\0"
    binary=struct.pack("<6I",1,0,0,0,1,1)+bytes(40)+struct.pack("<ffII",10,20,offsets["hash"],offsets["2.1.27.scsp"])
    binary+=struct.pack("<6f5IH",0,0,0,0,1,1,0,0,1,1,offsets["root"],65535)
    binary+=struct.pack("<IIifI",0xffffffff,offsets["ping"],7,2.5,0xffffffff)
    if timeline is None:
        timeline=struct.pack("<IIfifIH",5,1,0.25,42,3.75,0xffffffff,0)
    binary+=struct.pack("<IfI",offsets["idle"],1,1)+timeline
    raw=struct.pack("<II",len(binary),len(pool))+binary+pool
    packed=lz4.block.compress(raw,store_size=False)
    return struct.pack("<II",len(raw),len(packed))+packed


class SpineRegressionTests(unittest.TestCase):
    def test_v2_event_type5_preserves_payload_and_version(self):
        document,report=convert.decode_scsp(fixture_v2())
        self.assertEqual(document["skeleton"]["spine"],"2.1.27")
        event=document["animations"]["idle"]["events"][0]
        self.assertEqual(event,{"time":0.25,"name":"ping","int":42,"float":3.75,"string":""})
        self.assertEqual(report["binaryBytes"],report["consumedBytes"])
    def test_unknown_timeline_does_not_emit_partial_animation(self):
        with self.assertRaisesRegex(ValueError,"timeline"):
            convert.decode_scsp(fixture_v2(struct.pack("<I",1234)))
    def test_bezier_and_linear_curves_are_not_forced_stepped(self):
        curve=struct.pack("<HBIf4f",0,2,0,0.25,0.1,0.2,0.8,0.9)
        # Curve carries four controls after the cached 4-byte segment field.
        reader=convert.Reader(struct.pack("<HBI4f",0,2,0,0.1,0.2,0.8,0.9))
        values=convert._curves_v2(reader,2)[0]["curve"]
        for actual,expected in zip(values,[0.1,0.2,0.8,0.9]): self.assertAlmostEqual(actual,expected,6)
        self.assertEqual(convert._curves_v2(convert.Reader(b"\xff\xff"),2),[{}])
    def test_curve_v3_recovers_controls_from_samples(self):
        samples=[2]
        controls=[.25,0,.75,1]
        for i in range(1,10):
            t=i/10
            samples.extend([3*(1-t)**2*t*controls[k]+3*(1-t)*t*t*controls[k+2]+t**3 for k in (0,1)])
        value=convert._curve_v3(samples,0)
        recovered=[value["curve"],value["c2"],value["c3"],value["c4"]]
        for actual,expected in zip(recovered,controls):self.assertAlmostEqual(actual,expected,5)
    def test_curve_v3_rejects_non_cubic_samples(self):
        with self.assertRaisesRegex(ValueError,"cubic"):
            convert._curve_v3([2]+[.9,.1]*9,0)
    def test_rejects_unknown_curve(self):
        with self.assertRaises(ValueError): convert._curves_v2(convert.Reader(b"\0\0\x09"),2)
    def test_draw_order_preserves_permutation(self):
        from types import SimpleNamespace
        slots=[SimpleNamespace(name=n) for n in ["a","b","c"]]
        offsets=convert._draw_order([2,0,1],slots)
        self.assertEqual([entry["offset"] for entry in offsets],[1,1,-2])
        with self.assertRaises(ValueError): convert._draw_order([0,0,1],slots)
    def test_sct2_rejects_unknown_format(self):
        header=bytearray(72);header[:4]=b"SCT2";struct.pack_into("<I",header,12,72)
        struct.pack_into("<IHH",header,20,987,4,4)
        with self.assertRaisesRegex(ValueError,"format"):convert.decode_sct(bytes(header)+bytes(16))
    def test_sct2_raw_preserves_rgba(self):
        header=bytearray(72);header[:4]=b"SCT2";struct.pack_into("<I",header,12,72)
        struct.pack_into("<IHH",header,20,44,1,1)
        image,report=convert.decode_sct(bytes(header)+bytes([20,40,60,80]))
        self.assertEqual(image.getpixel((0,0)),(20,40,60,80))


if __name__ == "__main__": unittest.main()
