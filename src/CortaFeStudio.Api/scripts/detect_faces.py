import json, sys

def main():
    import cv2
    video, start, end, output = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    cap = cv2.VideoCapture(video); duration = max(1, end - start); centers = []
    for i in range(12):
        cap.set(cv2.CAP_PROP_POS_MSEC, (start + duration * (i + .5) / 12) * 1000)
        ok, frame = cap.read()
        if not ok: continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, scaleFactor=1.12, minNeighbors=5, minSize=(60, 60))
        if len(faces):
            x, y, w, h = max(faces, key=lambda face: face[2] * face[3]); centers.append((x + w / 2) / frame.shape[1])
    cap.release(); result = {"detected": bool(centers), "cropX": sum(centers) / len(centers) if centers else .5, "samples": len(centers)}
    with open(output, "w", encoding="utf-8") as handle: json.dump(result, handle)
if __name__ == "__main__": main()
