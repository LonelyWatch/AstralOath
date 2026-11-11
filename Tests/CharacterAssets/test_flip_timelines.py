"""Native 2.1 timeline IDs: 9=flipX, 10=flipY (not auxiliary draw order).
Official fixed upstream: 142e770e5b4b42395a3a0a68ff3c8a21ba2774eb,
spine-c/include/spine/Animation.h (spTimelineType, spFlipTimeline).
"""
import math, struct, unittest
from unittest.mock import patch
from test_conversion import fixture_v2
import convert

def flip(kind, frames=((0.,0.),(.25,1.),(1.,0.)), bone=0, axis=None, count=None):
    values=[v for frame in frames for v in frame]
    return struct.pack('<4I',kind,bone,kind==9 if axis is None else axis,len(values) if count is None else count)+struct.pack('<'+'f'*len(values),*values)

class NativeFlipTimelineTests(unittest.TestCase):
    def test_flip_x_is_a_bone_timeline_even_when_no_slots_exist(self):
        doc,report=convert.decode_scsp(fixture_v2(flip(9)))
        self.assertEqual(doc['animations']['idle']['bones']['root']['flipX'],
                         [{'time':0.,'x':False},{'time':.25,'x':True},{'time':1.,'x':False}])
        self.assertEqual(report['sourceTimelineCounts'],{'idle':{'9':1}})
        self.assertEqual(report['engineDrawOrder'],{})
        self.assertEqual(report['converterVersion'],'astraloath-scsp/2')
        self.assertEqual(report['boneFlipTimelines'],{'idle':{'flipX':{'tracks':1,'frames':3,'bones':{'root':3}},'flipY':{'tracks':0,'frames':0,'bones':{}}}})
    def test_flip_y_is_not_an_event_and_preserves_axis(self):
        doc,report=convert.decode_scsp(fixture_v2(flip(10)))
        self.assertEqual(doc['animations']['idle']['bones']['root']['flipY'],
                         [{'time':0.,'y':False},{'time':.25,'y':True},{'time':1.,'y':False}])
        self.assertNotIn('events',doc['animations']['idle'])
    def test_both_flip_axes_coexist_with_native_draw_order(self):
        draw=struct.pack('<IIIfB',6,0,1,0.,0)
        doc,report=convert.decode_scsp(fixture_v2(flip(9)+flip(10)+draw,3))
        self.assertEqual(set(doc['animations']['idle']['bones']['root']),{'flipX','flipY'})
        self.assertEqual(doc['animations']['idle']['drawOrder'],[{'time':0.}])
        self.assertEqual(report['sourceTimelineCounts'],{'idle':{'9':1,'10':1,'6':1}})
        self.assertEqual(report['binaryBytes'],report['consumedBytes'])
    def test_rejects_wrong_axis_marker(self):
        for kind in (9,10):
            with self.subTest(kind=kind), self.assertRaisesRegex(ValueError,'[Ff]lip'):
                convert.decode_scsp(fixture_v2(flip(kind,axis=kind==10)))
    def test_rejects_invalid_bone_index(self):
        with self.assertRaisesRegex(ValueError,'[Ff]lip'):
            convert.decode_scsp(fixture_v2(flip(9,bone=1)))
    def test_rejects_odd_or_empty_frame_array(self):
        for count in (0,3):
            with self.subTest(count=count), self.assertRaisesRegex(ValueError,'[Ff]lip'):
                convert.decode_scsp(fixture_v2(flip(9,count=count)))
    def test_rejects_non_boolean_values(self):
        for value in (.5,-1,2,float('nan'),float('inf')):
            with self.subTest(value=value), self.assertRaisesRegex(ValueError,'[Ff]lip'):
                convert.decode_scsp(fixture_v2(flip(9,((0,value),))))
    def test_rejects_negative_nonfinite_or_backwards_times(self):
        for times in ((-1.,),(float('nan'),),(float('inf'),),(1.,0.)):
            with self.subTest(times=times), self.assertRaisesRegex(ValueError,'[Ff]lip'):
                convert.decode_scsp(fixture_v2(flip(9,tuple((t,1) for t in times))))
    def test_rejects_truncated_flip_payload(self):
        with self.assertRaises(ValueError):
            convert.decode_scsp(fixture_v2(flip(9)[:-1]))
    def test_duplicate_flip_track_cannot_silently_replace_frames(self):
        with self.assertRaisesRegex(ValueError,'[Ff]lip'):
            convert.decode_scsp(fixture_v2(flip(9)+flip(9),2))


    def test_writer_must_not_drop_flip_tracks_or_frames(self):
        original=convert.writer.write_json_data
        for remove_track in (True,False):
            def omit(sk):
                doc=original(sk)
                bone=doc['animations']['idle'].get('bones',{}).get('root',{})
                if remove_track: bone.pop('flipX',None)
                elif 'flipX' in bone: bone['flipX'].pop()
                return doc
            with self.subTest(remove_track=remove_track), patch.object(convert.writer,'write_json_data',omit), self.assertRaisesRegex(ValueError,'[Ff]lip'):
                convert.decode_scsp(fixture_v2(flip(9)))
