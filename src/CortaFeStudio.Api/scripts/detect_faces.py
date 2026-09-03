import json, sys


def participant_side(center):
    if center < .44:
        return "left"
    if center > .56:
        return "right"
    return "center"


def mouth_motion(cv2, gray, face, previous_mouth):
    x, y, w, h = face
    x1, x2 = x + int(w * .16), x + int(w * .84)
    y1, y2 = y + int(h * .52), y + int(h * .94)
    mouth = gray[y1:y2, x1:x2]
    if mouth.size == 0:
        return 0, previous_mouth
    mouth = cv2.resize(mouth, (48, 28))
    mouth = cv2.GaussianBlur(mouth, (5, 5), 0)
    motion = 0 if previous_mouth is None else float(cv2.mean(cv2.absdiff(mouth, previous_mouth))[0]) / 255
    return motion, mouth


def main():
    import cv2
    video, start, end, output = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
    content_type = sys.argv[5].lower() if len(sys.argv) > 5 else ""
    is_podcast = content_type in ("podcast", "entrevista")
    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    cap = cv2.VideoCapture(video); duration = max(1, end - start); observations = []
    sample_count = max(18 if is_podcast else 12, min(48 if is_podcast else 30, round(duration / 1.4 if is_podcast else duration / 2)))
    previous = None; active_side = None; areas = []; scene_changes = 0; scene_times = []; previous_histogram = None
    previous_mouths = {}; multi_person_samples = 0; decisive_samples = 0; speaker_switches = 0
    side_counts = {"left": 0, "center": 0, "right": 0}
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
            scene_times.append(round(relative_time, 3))
        previous_histogram = histogram
        faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(60, 60))
        if len(faces):
            candidates = []
            max_area = max(w * h for x, y, w, h in faces)
            for face in faces:
                x, y, w, h = face
                center = (x + w / 2) / frame.shape[1]
                side = participant_side(center)
                motion, mouth = mouth_motion(cv2, gray, face, previous_mouths.get(side))
                previous_mouths[side] = mouth
                candidates.append({"center": center, "area": w * h, "area_score": w * h / max_area, "motion": motion, "side": side})

            separated = len(candidates) > 1 and max(c["center"] for c in candidates) - min(c["center"] for c in candidates) >= .25
            if is_podcast and separated:
                multi_person_samples += 1
                for candidate in candidates:
                    candidate["score"] = candidate["motion"] * 5 + candidate["area_score"] * .18 + (.12 if candidate["side"] == active_side else 0)
                ranked = sorted(candidates, key=lambda candidate: candidate["score"], reverse=True)
                chosen = ranked[0]
                same_side = next((candidate for candidate in candidates if candidate["side"] == active_side), None)
                if same_side is not None and chosen["side"] != active_side and chosen["score"] - same_side["score"] < .10:
                    chosen = same_side
                elif len(ranked) > 1 and ranked[0]["motion"] - ranked[1]["motion"] > .018:
                    decisive_samples += 1
                new_side = chosen["side"]
                if active_side is not None and new_side != active_side:
                    speaker_switches += 1
                active_side = new_side
            else:
                chosen = max(candidates, key=lambda candidate: candidate["area"] * (1.0 if previous is None else max(.35, 1 - abs(candidate["center"] - previous))))
                active_side = chosen["side"]

            center, area = chosen["center"], chosen["area"]
            areas.append(area / (frame.shape[0] * frame.shape[1]))
            previous = center if previous is None or abs(center - previous) >= .22 else previous * .68 + center * .32
            side_counts[participant_side(previous)] += 1
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
        elif previous is not None:
            side_counts[participant_side(previous)] += 1
            observations.append({"time": round(relative_time, 3), "x": round(previous, 4)})
    cap.release()
    centers = [point["x"] for point in observations]
    movement = sum(abs(centers[i] - centers[i - 1]) for i in range(1, len(centers))) / max(1, len(centers) - 1)
    dominant_side = max(side_counts, key=side_counts.get)
    dominant_centers = [center for center in centers if participant_side(center) == dominant_side]
    crop_x = sum(dominant_centers) / len(dominant_centers) if dominant_centers else (sum(centers) / len(centers) if centers else .5)
    result = {
        "detected": bool(centers), "cropX": crop_x,
        "samples": len(centers), "sampleCount": sample_count,
        "coverage": len(centers) / sample_count, "stability": max(0, 1 - movement * 4),
        "prominence": sum(areas) / len(areas) if areas else 0,
        "sceneChanges": scene_changes, "sceneTimes": scene_times, "track": observations,
        "multiPerson": multi_person_samples > 0, "multiPersonSamples": multi_person_samples,
        "speakerSwitches": speaker_switches,
        "activeSpeakerConfidence": decisive_samples / multi_person_samples if multi_person_samples else 0
    }
    with open(output, "w", encoding="utf-8") as handle: json.dump(result, handle)


if __name__ == "__main__": main()
