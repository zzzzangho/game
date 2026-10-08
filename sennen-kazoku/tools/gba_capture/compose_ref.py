#!/usr/bin/env python3
"""C# 조합기 대조용 기준값(로컬 전용): 여러 외형·연령 칸·자세의 조합 결과 해시를 json 으로 남긴다.
사용: python3 -I compose_ref.py <rom.gba> <출력.json>"""
import hashlib, json, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import romparts as R, charcompose as C

if __name__ == "__main__":
    lib = C.Library(R.load(sys.argv[1])); rnd = random.Random(3); out = []
    for i in range(60):
        look = {"body": rnd.randrange(24), "face": rnd.randrange(25), "hair": rnd.randrange(104), "eyes": rnd.randrange(122),
                "nose": rnd.randrange(38), "mouth": rnd.randrange(55), "eyesFlip": rnd.randrange(2), "noseFlip": rnd.randrange(2), "mouthFlip": rnd.randrange(2)}
        ag = rnd.choice([{"body": 0, "face": 0, "hair": 0, "feat": 0}, {"body": 1, "face": 1, "hair": 1, "feat": 1}, {"body": 2, "face": 1, "hair": 1, "feat": 2}])
        pose = rnd.randrange(4); st = rnd.randrange(4)
        px = C.render(lib, dict(look, body=48 + look["body"] if False else look["body"]), ag, pose, st)
        out.append({"look": look, "age": ag, "pose": pose, "set": st, "md5": hashlib.md5(bytes(px)).hexdigest()})
    json.dump(out, open(sys.argv[2], "w"))
    print(len(out))
