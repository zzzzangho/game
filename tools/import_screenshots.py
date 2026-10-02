#!/usr/bin/env python3
"""Rebuild the local (git-ignored) art in assets/ from anime screenshots.

    python3 tools/import_screenshots.py            # download the posts' screenshots, then crop
    python3 tools/import_screenshots.py --no-download

Screenshots are cached in build/screens/p1..p4. Only crop coordinates live here; the images themselves
stay out of git. The build (tools/art.py) turns these crops into pixel art (KMT_PIXEL=chunky|native|off).
"""
import argparse
import os
import re
import subprocess
import urllib.request

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
CACHE = os.path.join(ROOT, "build", "screens")
POSTS = ["3504144", "3504159", "3504175", "3504204"]  # File 1-4
UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36"
IMG_RE = re.compile(r"https://blog\.kakaocdn\.net/dn/[A-Za-z0-9]+/[A-Za-z0-9]+/[A-Za-z0-9]+/img\.jpg")


def fetch(url, referer=None):
    req = urllib.request.Request(url, headers={"User-Agent": UA, **({"Referer": referer} if referer else {})})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def download():
    for part, post in enumerate(POSTS, 1):
        page = f"https://cafe.daum.net/_c21_/bbs_search_read?grpid=1IHuH&fldid=ReHf&datanum={post}&q=t"
        html = fetch(page, f"https://cafe.daum.net/subdued20club/ReHf/{post}").decode("utf-8", "replace")
        urls = list(dict.fromkeys(IMG_RE.findall(html)))
        d = os.path.join(CACHE, f"p{part}")
        os.makedirs(d, exist_ok=True)
        jobs = []
        for i, u in enumerate(urls, 1):
            f = os.path.join(d, f"{i:03d}.jpg")
            if not os.path.exists(f) or os.path.getsize(f) == 0:
                jobs.append(f"{u} {f}")
        if jobs:
            subprocess.run(["xargs", "-P", "8", "-n", "2", "sh", "-c", 'curl -sS -L --retry 3 -o "$2" "$1"', "_"],
                           input="\n".join(jobs), text=True, check=False)
        print(f"part {part}: {len(urls)} screenshots")


def P(x0, w=427):  # full-height bust box starting at x0
    return (x0, 0, x0 + w, 480)


PORTRAITS = {  # frontal shots, 128:144 boxes; each crop is cut out with the anime segmentation model
    "kin": ("p4/016", P(100)),
    "kin_serious": ("p4/053", P(130)),
    "kin_surprised": ("p1/350", P(106)),
    "miyuki": ("p1/104", (300, 40, 640, 422)),
    "miyuki_surprised": ("p2/289", P(80)),
    "saki": ("p1/119", (40, 20, 420, 447)),
    "kenmochi": ("p4/358", (40, 30, 440, 480)),
    "akechi": ("p4/367", P(107)),
    "yamagami": ("p1/126", P(107)),
    "yumi": ("p2/255", P(107)),
    "sakonji": ("p2/257", P(107)),
    "yurama": ("p1/169", P(100)),
    "yurama_angry": ("p1/195", P(100)),
    "sakuraba": ("p4/267", P(107)),
    "takato": ("p4/161", P(107)),
    "takato_surprised": ("p2/256", P(107)),
    "takato_smile": ("p4/189", P(107)),
    "takato_shock": ("p4/230", P(107)),
    "takato_cold": ("p4/199", P(160)),
    "satomi": ("p4/003", (160, 0, 480, 360)),
    "nagasaki": ("p2/030", P(107)),
    "mario": ("p1/384", P(107)),
    "clown": ("p1/041", P(107)),
}


def S(y0):  # full-width 3:2 scene box starting at y0
    return (0, y0, 640, y0 + 427)


# Captures shown with @cut at key moments of the story (the regular backgrounds are drawn art).
CUTS = {
    "cut_parcel": ("p1/076", S(20)),      # the parcel opened at police HQ
    "cut_roses": ("p1/335", S(20)),       # Yamagami among the roses
    "cut_balloons": ("p1/332", S(20)),    # Saki's video: the hand held up by balloons
    "cut_mario": ("p2/027", S(20)),       # the masked guest checking out
    "cut_body": ("p1/407", S(20)),        # Yamagami hanging from strings (the shipped image is the user-supplied capture)
    "cut_vase": ("p2/109", S(20)),      # Yumi smashes the vase
    "cut_fall": ("p2/120", S(20)),      # Kindaichi trips on the drawbridge
    "cut_psychic": ("p2/130", S(20)),   # Sakuraba raises a drum by telekinesis
    "cut_drum": ("p2/131", S(20)),      # the drum floats over Sakuraba's head
    "cut_drumsticks": ("p2/132", S(20)), # the drumsticks beat by themselves
    "cut_cuffs": ("p2/133", S(20)),     # Yumi handcuffed under water
    "cut_tank": ("p2/134", S(20)),      # the mermaid locked in the water tank
    "cut_cloth": ("p2/135", S(20)),     # the tank under a black cloth
    "cut_empty": ("p2/136", S(20)),     # the empty tank
    "cut_dress": ("p2/137", S(20)),     # Yumi appears on the tank in a blue dress
    "cut_crowd": ("p2/138", S(20)),     # the cheering audience
    "cut_wink": ("p2/140", S(20)),      # Satomi winks at the audience
    "cut_filming": ("p2/141", S(20)),   # Saki filming, Kindaichi blushing
    "cut_glare": ("p2/142", S(20)),     # Miyuki glares at Kindaichi
    "cut_curtain": ("p2/129", S(20)),  # the curtain rises on the stage
    "cut_satomi_stage": ("p2/139", S(20)),  # Satomi alone on the stage
    "cut_mari_face": ("p2/145", S(20)),  # the marionette's painted face
    "cut_mari_scissors": ("p2/149", S(20)),  # the marionette raises the scissors
    "cut_mari_snip": ("p2/150", S(20)),  # the strings snipped
    "cut_mari_fall": ("p2/151", S(20)),  # the marionette collapses
    "cut_audience": ("p2/154", S(20)),  # Saki, Kindaichi and Miyuki watching
    "cut_bike": ("p2/158", S(20)),  # the marionette rides a bicycle
    "cut_tumble": ("p2/161", S(20)),  # the marionette tumbles about
    "cut_mari_worry": ("p2/162", S(20)),  # the marionette, worried
    "cut_mari_look": ("p2/165", S(20)),  # the marionette looks up
    "cut_mari_tired": ("p2/170", S(20)),  # the marionette back on its strings
    "cut_lights": ("p2/175", S(20)),  # the stage lights
    "cut_flash_petals": ("p2/180", S(20)),  # rose petals in the flash
    "cut_window_rose": ("p2/318", S(20)),  # the puppeteer at the hotel window, roses blowing in
    "cut_night_flight": ("p2/321", S(20)),  # the puppeteer floats in the night sky carrying 'Miyuki'
    "cut_carried": ("p2/323", S(20)),  # 'Miyuki' in the puppeteer's arms
    "cut_window_jump": ("p2/324", S(20)),  # Kindaichi climbs out of the window
    "cut_swamp_mask": ("p2/360", S(20)),  # the puppeteer's mask in the swamp
    "cut_swamp_under": ("p2/369", S(20)),  # Kindaichi sinks under the swamp water
    "cut_swamp_rose": ("p2/371", S(20)),  # the puppeteer smells a red rose
    "cut_rose_float": ("p2/374", S(20)),  # a red rose floating on the swamp
    "cut_rose_rain": ("p1/052", S(20)),  # red roses rain down in the berth car
    "cut_masked_bow": ("p1/053", S(20)),  # the masked magician bows at the end of the car
    "cut_rose_cheer": ("p1/056", S(20)),  # Miyuki and Saki cheer
    "cut_dawn": ("p1/086", S(20)),  # dawn over the rails
    "cut_gentle_walk": ("p1/127", S(20)),  # Gentle Yamagami walks down the dining car
    "cut_water_trick": ("p1/131", S(20)),  # Yumi raises the water in the glass like a pillar
    "cut_freight_stop": ("p1/231", S(20)),  # the train stops at the freight yard
    "cut_evacuate": ("p1/233", S(20)),  # passengers hurry off the train
    "cut_crowd_watch": ("p1/279", S(20)),  # everyone watches the train from afar
    "cut_flinch": ("p1/289", S(20)),  # Akechi and Kenmochi flinch at the blast
    "cut_rocket": ("p1/291", S(20)),  # something shoots into the sky
    "cut_lobby_bag": ("p1/378", S(20)),  # a big bundle tied with straps in the hotel lobby
    "cut_mario_bag": ("p1/390", S(20)),  # the masked guest's bundle in the fog
    "cut_mario_fog2": ("p1/391", S(20)),  # the masked guest walks off into the fog
    "cut_run_up": ("p3/301", S(20)),  # running up the hotel stairs
    "cut_door215": ("p3/303", S(20)),  # everyone at Yumi's door, room 215
    "cut_bash": ("p3/308", S(20)),  # Kenmochi rams the door
    "cut_burst_in": ("p3/309", S(20)),  # bursting into Yumi's room
    "cut_empty_room": ("p3/311", S(20)),  # Yumi's room, empty and dark
    "cut_window_curtain": ("p3/314", S(20)),  # the curtain blowing at the open window
    "cut_kin_window": ("p3/318", S(20)),  # Kindaichi looks out of the window
    "cut_branch_rope": ("p3/319", S(20)),  # a rope over the branch outside
    "cut_yumi_hanged": ("p3/322", S(20)),  # Yumi hanging from the tree
    "cut_feet": ("p3/325", S(20)),  # Yumi's dangling feet
    "cut_stairs_down": ("p3/338", S(20)),  # Kindaichi runs downstairs
    "cut_door115": ("p3/341", S(20)),  # room 115, right below
    "cut_lower_room": ("p3/343", S(20)),  # Kindaichi enters the room below
    "cut_balloon_jump": ("p3/100", S(20)),  # Kindaichi jumps for the balloon
    "cut_balloon_catch": ("p3/102", S(20)),  # Akechi catches the balloon string
    "cut_reenact_open": ("p3/151", S(20)),  # the cabin door opens on the roses
    "cut_reenact_hole": ("p3/150", S(20)),  # the hollow in the roses where the doll lay
    "cut_reiko_note": ("p3/390", S(20)),  # Reiko with her trick notebook on the catwalk
    "cut_reiko_fall": ("p3/391", S(20)),  # Reiko falls from the ceiling
    "cut_reiko_roses": ("p3/392", S(20)),  # Reiko among the shattered vase and roses
    "cut_face_sakonji": ("p3/410", S(20)),  # Sakonji, frowning
    "cut_face_takato": ("p3/412", S(20)),  # Takato, sweating
    "cut_face_satomi": ("p3/413", S(20)),  # Satomi, uneasy
    "cut_face_nagasaki": ("p3/414", S(20)),  # Nagasaki, eyes closed
    "cut_errand": ("p4/272", S(20)),  # Takato, the manager, carrying bags down a corridor
    "cut_dressing_peek": ("p4/273", S(20)),  # the troupe chatting in the dressing room
    "cut_sakonji_smoke": ("p4/274", S(20)),  # Sakonji smoking
    "cut_reiko_fall2": ("p4/276", S(20)),  # Reiko falling, five years ago
    "cut_yumi_whisper": ("p4/279", S(20)),  # Yumi whispering
    "cut_takato_listen": ("p4/280", S(20)),  # Takato listening behind the door
    "cut_fists": ("p4/281", S(20)),  # Takato's clenched fists
    "cut_glasses_toss": ("p4/167", S(20)),  # Takato takes off his glasses
    "cut_glasses_fly": ("p4/172", S(20)),  # the glasses fly through the air
    "cut_glasses_rose": ("p4/174", S(20)),  # the glasses become a red rose
    "cut_rose_burst": ("p4/175", S(20)),  # the rose bursts into petals
    "cut_takato_smile": ("p4/176", S(20)),  # Takato smiles among the petals
    "cut_park": ("p4/203", S(20)),  # the London park at sunset
    "cut_young_takato": ("p4/209", S(20)),  # young Takato
    "cut_show_audience": ("p4/251", S(20)),  # the Gensou troupe's audience in Japan
    "cut_show_sakuraba": ("p4/252", S(20)),  # Sakuraba on stage
    "cut_show_yumi": ("p4/253", S(20)),  # Yumi on stage
    "cut_show_sakonji": ("p4/254", S(20)),  # Sakonji on stage
    "cut_show_robert": ("p4/255", S(20)),  # Satomi and Robert on stage
    "cut_show_yurama": ("p4/256", S(20)),  # Yurama on stage
    "cut_show_gentle": ("p4/257", S(20)),  # Gentle Yamagami on stage
    "cut_takato_seat": ("p4/258", S(20)),  # Takato in the audience, stunned
    "cut_reiko_memory": ("p4/262", S(20)),  # Reiko in Takato's memory
    "cut_sinister": ("p4/263", S(20)),  # the disciples' faces, sinister
    "cut_takato_alone": ("p4/269", S(20)),  # Takato alone in the crowd
    "cut_note_drop": ("p4/286", S(20)),  # Sakonji drops the trick notebook
    "cut_rock_cuffs": ("p4/338", S(20)),  # Sakonji is handcuffed for the escape act
    "cut_rock_inside": ("p4/340", S(20)),  # Sakonji inside the rock-shaped box
    "cut_rock_crowd": ("p4/346", S(20)),  # the audience gasps at the floating rock
    "cut_rock_burnt": ("p4/353", S(20)),  # the burning box on the stage
    "cut_stage_police": ("p4/357", S(20)),  # police around the body on the stage
    "cut_cell_smile": ("p4/356", S(20)),  # Takato smiles in his cell
    "cut_note_pages": ("p4/388", S(20)),  # comparing the notebook pages
    "cut_takato_flames": ("p4/398", S(20)),  # Takato's silhouette in the flames
    "cut_kin_eye": ("p4/401", S(20)),  # Kindaichi's resolute eye
    "cut_satomi": ("p2/144", S(20)),      # Satomi as the living marionette, hanging from strings
    "cut_marionette": ("p2/152", S(20)),  # the living marionette dancing
    "cut_yurama": ("p2/188", S(20)),      # Yurama on the marionette chair under the red light
    "cut_fog": ("p1/392", S(20)),         # a figure walking in the fog
    "cut_sinking": ("p2/366", S(20)),     # Kindaichi sinking in the swamp
    "cut_jade": ("p4/013", S(20)),        # the heavy jade stone
    "cut_bag": ("p4/118", S(20)),         # the small bag with the false bottom
    "cut_takato": ("p4/166", S(0)),       # Takato adjusts his glasses
    "cut_reiko": ("p4/217", S(0)),        # Reiko in the London park
    "cut_rock": ("p4/331", S(20)),        # Sakonji's floating rock
    "cut_fire": ("p4/349", S(20)),        # the rock in flames
}

ICONS = {
    "puppet": ("p1/085", (40, 0, 600, 480)),
}


def crop(src, box):
    im = Image.open(os.path.join(CACHE, src + ".jpg")).convert("RGB")
    return im.crop(box)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-download", action="store_true")
    args = ap.parse_args()
    if not args.no_download:
        download()
    for sub, table in (("portraits", PORTRAITS), ("cuts", CUTS), ("icons", ICONS)):
        d = os.path.join(ROOT, "assets", sub)
        os.makedirs(d, exist_ok=True)
        for key, (src, box) in table.items():
            im = crop(src, box)
            if sub != "cuts":
                im = im.convert("RGBA")  # opaque RGBA: the build pixelizes it and frames it
            im.save(os.path.join(d, key + ".png"))
        print(sub, len(table))


if __name__ == "__main__":
    main()
