import json, sys

def main():
    import cv2
    video, start, end, output = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    cap = cv2.VideoCapture(video); duration = max(1, end - start); observations = []
    sample_count = max(12, min(30, round(duration / 2)))
    previous = None
    for i in range(sample_count):
        relative_time = duration * i / max(1, sample_count - 1)
        cap.set(cv2.CAP_PROP_POS_MSEC, (start + relative_time) * 1000)
        ok, frame = cap.read()
        if not ok: continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, scaleFactor=1.12, minNeighbors=5, minSize=(60, 60))
        if len(faces):
            candidates = [((x + w / 2) / frame.shape[1], w * h) for x, y, w, h in faces]
            center, area = max(candidates, key=lambda item: item[1] * (1.0 if previous is None else max(.35, 1 - abs(item[0] - previous))))
            previous = center if previous is None else previous * .72 + center * .28
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
        elif previous is not None:
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
    cap.release()
    centers = [point["x"] for point in observations]
    result = {"detected": bool(centers), "cropX": sum(centers) / len(centers) if centers else .5, "samples": len(centers), "track": observations}
    with open(output, "w", encoding="utf-8") as handle: json.dump(result, handle)
if __name__ == "__main__": main()
