"""E7 v2 sentinel curves and float Bezier target indices (independent wire fixtures)."""
import math, struct, unittest
from unittest.mock import patch
from test_conversion import fixture_v2
import convert

def translate(block, count=3):
    frames=[value for i in range(count) for value in (i*.5,i*10.,0.)]
    return struct.pack('<3I',2,0,len(frames))+struct.pack('<'+'f'*len(frames),*frames)+block

def bezier(index, controls=(.125,.25,.75,.875)):
    return struct.pack('<B5f',2,index,*controls)

class NativeCurveTests(unittest.TestCase):
    def test_ffff_is_linear_and_fffe_is_stepped(self):
        for marker,value in ((65535,{}),(65534,{'curve':'stepped'})):
            with self.subTest(marker=marker):
                self.assertEqual(convert._curves_v2(convert.Reader(struct.pack('<H',marker)),3),[value,value])
    def test_compact_stepped_survives_writer_and_has_source_report(self):
        doc,report=convert.decode_scsp(fixture_v2(translate(struct.pack('<H',65534))))
        frames=doc['animations']['idle']['bones']['root']['translate']
        self.assertEqual([f.get('curve') for f in frames],['stepped','stepped',None])
        self.assertEqual(report['converterVersion'],'astraloath-scsp/3')
        self.assertEqual(report['curveTimelines']['idle'],{'tracks':1,'frames':3,'segments':2,'sourceMarkers':{'allLinear':0,'allStepped':1,'explicit':0},'modes':{'linear':0,'stepped':2,'bezier':0}})
    def test_bezier_target_is_float_frame_index_not_record_index(self):
        block=struct.pack('<H',3)+bezier(1)+bytes([0])
        value=convert._curves_v2(convert.Reader(block),3)
        self.assertEqual(value,[{}, {'curve':[.125,.25,.75,.875]}])
    def test_explicit_linear_stepped_and_bezier_keep_wire_order(self):
        block=struct.pack('<H',4)+bezier(2)+bytes([1,0])
        self.assertEqual(convert._curves_v2(convert.Reader(block),4),[{}, {'curve':'stepped'}, {'curve':[.125,.25,.75,.875]}])
    def test_explicit_curve_count_mismatch_fails_closed(self):
        for count in (0,1,2,4):
            with self.subTest(count=count),self.assertRaisesRegex(ValueError,'[Cc]urve'):
                convert._curves_v2(convert.Reader(struct.pack('<H',count)+bytes(4)),3)
    def test_invalid_bezier_indices_fail_closed(self):
        for index in (-1.,.5,2.,float('nan'),float('inf')):
            with self.subTest(index=index),self.assertRaisesRegex(ValueError,'[Cc]urve'):
                convert._curves_v2(convert.Reader(struct.pack('<H',3)+bezier(index)+bytes([0])),3)
    def test_nonfinite_bezier_controls_fail_closed(self):
        for bad in (float('nan'),float('inf')):
            with self.subTest(bad=bad),self.assertRaisesRegex(ValueError,'[Cc]urve'):
                convert._curves_v2(convert.Reader(struct.pack('<H',2)+bezier(0,(bad,0,1,1))),2)
    def test_invalid_curve_type_fails_closed(self):
        with self.assertRaisesRegex(ValueError,'[Cc]urve'):
            convert._curves_v2(convert.Reader(struct.pack('<HB',2,3)),2)
    def test_writer_cannot_drop_curve_or_frame(self):
        original=convert.writer.write_json_data
        for action in ('curve','frame'):
            def damaged(sk):
                doc=original(sk);frames=doc['animations']['idle']['bones']['root']['translate']
                if action=='curve':frames[0].pop('curve',None)
                else:frames.pop()
                return doc
            with self.subTest(action=action),patch.object(convert.writer,'write_json_data',damaged),self.assertRaisesRegex(ValueError,'[Cc]urve'):
                convert.decode_scsp(fixture_v2(translate(struct.pack('<H',65534))))
    def test_writer_cannot_change_bezier_target(self):
        original=convert.writer.write_json_data
        def damaged(sk):
            doc=original(sk);frames=doc['animations']['idle']['bones']['root']['translate']
            frames[0]['curve']=[.125,.25,.75,.875];frames[1].pop('curve',None)
            return doc
        with patch.object(convert.writer,'write_json_data',damaged),self.assertRaisesRegex(ValueError,'[Cc]urve'):
            convert.decode_scsp(fixture_v2(translate(struct.pack('<H',3)+bezier(1)+bytes([0]))))
