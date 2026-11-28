"""Compare fresh ChannelSummonInformationTests captures against the measured phone frame."""
import argparse
import json
from pathlib import Path
from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture_directory', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    measured = []
    issues = []

    def check(state, suffix, expected, tolerance, right=False, painted=False, edges=(0, 1, 2, 3), aspect=None):
        data = json.loads((args.capture_directory / (state + '.bounds.json')).read_text(encoding='utf-8-sig'))
        nodes = [r for r in data['elements'] if r['path'].endswith(suffix)]
        if len(nodes) != 1:
            raise ValueError(f'{state} needs one node ending {suffix!r}, found {len(nodes)}')
        node = nodes[0]
        w, h = data['width'], data['height']
        ratio = h / 1200
        # x in the original 2608-wide recording: actual game spans [144,2608].
        origin = w - 2608 * ratio if right else w / 2 - 1376 * ratio
        target = [expected[i] * ratio + (origin if i in (0, 2) else 0) for i in range(4)]
        x1, x2 = sorted((node['x'], node['x'] + node['width']))
        y1, y2 = sorted((node['y'], node['y'] + node['height']))
        if aspect:
            # UGUI preserveAspect letterboxes the physical sprite inside its RectTransform.
            height = min(y2 - y1, (x2 - x1) / aspect)
            cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
            x1, x2 = cx - height * aspect / 2, cx + height * aspect / 2
            y1, y2 = cy - height / 2, cy + height / 2
        box = [x1,y1,x2,y2]
        if painted:
            with Image.open(args.capture_directory / (state + '.png')) as im:
                # A fixed column near the right of both original bubbles contains uninterrupted cream fill.
                col = round(box[0] + (box[2] - box[0]) * .87)
                rows = [y for y in range(max(0, round(box[1])), min(h, round(box[3])))
                        if all(c > threshold for c, threshold in zip(im.getpixel((col, y))[:3], (232, 228, 214)))]
                if not rows: raise ValueError(f'{state} {suffix} has no cream pixels')
                box[1], box[3] = min(rows), max(rows) + 1
                # Scan below the text center to find the fill underneath both ends.
                y = round(box[1] + (box[3] - box[1]) * .72)
                columns = [x for x in range(max(0, round(box[0])), min(w, round(box[2])))
                           if all(c > threshold for c, threshold in zip(im.getpixel((x, y))[:3], (232, 228, 214)))]
                if not columns: raise ValueError(f'{state} {suffix} has no cream span')
                box[0], box[2] = min(columns), max(columns) + 1
        delta = [round((box[i] - target[i]) / ratio, 1) for i in range(4)]
        passed = all(abs(delta[i]) <= tolerance for i in edges)
        if not passed: issues.append(suffix)
        measured.append(dict(state=state, node=suffix, referenceBounds=expected, projectedPixels=[round(x, 1) for x in target],
                             capturedPixels=[round(x, 1) for x in box], deltaAt1200=delta, checkedEdges=list(edges),
                             toleranceAt1200=tolerance, result='pass' if passed else 'outside'))

    # Bubble limits are thresholded on the actual reference at x=2450 and across its lower half.
    check('01-channel','/Message 1/Summon announcement',[1785,755,2567,845],12,right=True,painted=True)
    check('01-channel','/Message 2/Summon announcement',[1785,947,2567,1038],12,right=True,painted=True)
    check('02-hero','/Information/Illustration surface',[688,85,2058,1110],12)
    check('02-hero','/Information/Hero level',[737,321,935,377],15)
    check('02-hero','/Information/Imprint formation',[732,537,838,626],16)
    check('02-hero','/Information/Inspect imprint',[1054,535,1126,627],16)
    check('02-hero','/Information/Skill 0',[738,698,862,820],18)
    check('04-artifact','/Information/Card illustration',[1091,85,1436,634],13)
    check('04-artifact','/Information/skill',[1090,652,1376,712],14)
    # Text height depends on the artifact. The upper-left origin is invariant.
    check('04-artifact','/Information/Skill and story description',[1096,730,1660,980],16,edges=(0,1))
    check('05-artifact-painting','/Oriented painting page/Full painting',[1013,26,1740,1174],12,aspect=378/598)
    check('06-artifact-rotated','/Oriented painting page/Full painting',[598,110,2144,1090],14,aspect=598/378)
    # Portrait controls are anchored to the right edge after the entire page rotates.
    check('06-artifact-rotated','/Oriented painting page/Rotate',[2237,114,2324,358],16,right=True)
    check('06-artifact-rotated','/Oriented painting page/Gesture hint',[2335,111,2556,1092],16,right=True)
    report = dict(source='Screenrecorder-2026-11-28-20-01-10-182.mp4', visibleSource=[144,0,2608,1200],
                  captureDirectory=str(args.capture_directory), checks=measured, failed=issues)
    output = json.dumps(report,ensure_ascii=False,indent=2)
    print(output)
    if args.output: args.output.write_text(output + '\n',encoding='utf-8')
    return int(bool(issues))


if __name__ == '__main__':
    raise SystemExit(main())
