import json, sys

def main():
    import cv2
    video, start, end, output = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    cap = cv2.VideoCapture(video); duration = max(1, end - start); observations = []
    sample_count = max(12, min(30, round(duration / 2)))
    previous = None; areas = []; scene_changes = 0; previous_histogram = None
    for i in range(sample_count):
        relative_time = duration * i / max(1, sample_count - 1)
        cap.set(cv2.CAP_PROP_POS_MSEC, (start + relative_time) * 1000)
        ok, frame = cap.read()
        if not ok: continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        histogram = cv2.calcHist([gray], [0], None, [32], [0, 256])
        cv2.normalize(histogram, histogram)
        if previous_histogram is not None and cv2.compareHist(previous_histogram, histogram, cv2.HISTCMP_BHATTACHARYYA) > .42:
            scene_changes += 1
        previous_histogram = histogram
        faces = cascade.detectMultiScale(gray, scaleFactor=1.12, minNeighbors=5, minSize=(60, 60))
        if len(faces):
            candidates = [((x + w / 2) / frame.shape[1], w * h) for x, y, w, h in faces]
            center, area = max(candidates, key=lambda item: item[1] * (1.0 if previous is None else max(.35, 1 - abs(item[0] - previous))))
            areas.append(area / (frame.shape[0] * frame.shape[1]))
            previous = center if previous is None else previous * .72 + center * .28
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
        elif previous is not None:
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
    cap.release()
    centers = [point["x"] for point in observations]
    movement = sum(abs(centers[i] - centers[i - 1]) for i in range(1, len(centers))) / max(1, len(centers) - 1)
    result = {
        "detected": bool(centers), "cropX": sum(centers) / len(centers) if centers else .5,
        "samples": len(centers), "sampleCount": sample_count,
        "coverage": len(centers) / sample_count, "stability": max(0, 1 - movement * 4),
        "prominence": sum(areas) / len(areas) if areas else 0,
        "sceneChanges": scene_changes, "track": observations
    }
    with open(output, "w", encoding="utf-8") as handle: json.dump(result, handle)
if __name__ == "__main__": main()
