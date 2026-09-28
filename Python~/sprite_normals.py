#!/usr/bin/env python3
"""Sprite Maps Generator: AI normal maps (Marigold-Normals) for sprites and Spine atlas pages.

Usage:
    python sprite_normals.py <jobs.json>

jobs.json:
    {"model": str, "threads": int, "jobs": [{"source", "atlas", "output", "resolution", "ensembleSize",
      "steps", "normalGain", "edgeWidth", "edgeStrength", "edgeBlend"}]}

Output normals are in Unity tangent space (+X right, +Y up) and hybrid: network normals inside the shape,
distance-based bevel near the silhouette. For Spine atlas pages every region is processed separately in its
unrotated orientation and written back without rotating the vectors (the glint shader reads normals in the
region's screen basis, not in atlas UV basis).

Stdout protocol: "PROGRESS <index> <total> <name>", "DONE <output>", "ERROR <message>".
"""

import json
import os
import sys
import traceback
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

BACKGROUND = (128, 128, 128)
ALPHA_THRESHOLD = 0.1
MODEL_AXIS_SIGNS = (1.0, 1.0)


def configure_cpu(threads):
    os.environ.setdefault("MIOPEN_FIND_MODE", "FAST")
    os.environ["OMP_NUM_THREADS"] = str(threads)
    os.environ["MKL_NUM_THREADS"] = str(threads)
    try:
        os.nice(10)
    except (AttributeError, OSError):
        pass


def pick_device(threads):
    import torch

    torch.set_num_threads(threads)
    if not torch.cuda.is_available():
        print("[info] GPU is not available, running on CPU", flush=True)
        return "cpu"

    best = max(range(torch.cuda.device_count()), key=lambda i: torch.cuda.get_device_properties(i).total_memory)
    print(f"[info] GPU: {torch.cuda.get_device_name(best)}", flush=True)
    return f"cuda:{best}"


def load_pipeline(model, device):
    import torch
    from diffusers import MarigoldNormalsPipeline

    dtype = torch.float16 if device != "cpu" else torch.float32
    pipe = MarigoldNormalsPipeline.from_pretrained(
        model,
        variant="fp16" if dtype == torch.float16 else None,
        torch_dtype=dtype,
    )
    pipe.set_progress_bar_config(disable=True)
    return pipe.to(device)


def prepare_input(rgba, side):
    """Fits the sprite into a fixed square so GPU kernels are compiled only once."""
    alpha = rgba[..., 3:4] / 255.0
    rgb = rgba[..., :3] * alpha + np.array(BACKGROUND) * (1.0 - alpha)
    image = Image.fromarray(rgb.astype(np.uint8))

    scale = side / max(image.size)
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    offset = ((side - size[0]) // 2, (side - size[1]) // 2)

    canvas = Image.new("RGB", (side, side), BACKGROUND)
    canvas.paste(image.resize(size, Image.LANCZOS), offset)
    return canvas, (offset[0], offset[1], offset[0] + size[0], offset[1] + size[1])


def predict_normals(pipe, image, ensemble_size, steps):
    import torch

    generator = torch.Generator(device=pipe.device).manual_seed(0)
    result = pipe(
        image,
        num_inference_steps=steps,
        ensemble_size=ensemble_size,
        processing_resolution=0,
        match_input_resolution=True,
        generator=generator,
    )
    return np.asarray(result.prediction[0], dtype=np.float32)


def resize_normals(normals, width, height):
    channels = [
        np.asarray(Image.fromarray(normals[..., c]).resize((width, height), Image.BILINEAR))
        for c in range(3)
    ]
    stacked = np.stack(channels, axis=-1)
    return normalize(stacked)


def normalize(normals):
    return normals / np.maximum(np.linalg.norm(normals, axis=-1, keepdims=True), 1e-6)


def bevel_normals(mask, width, strength):
    distance = ndimage.distance_transform_edt(mask)
    distance = ndimage.gaussian_filter(distance, 1.5)
    t = np.clip(distance / width, 0.0, 1.0)
    height = np.sqrt(1.0 - (1.0 - t) ** 2) * strength

    grad_y_down, grad_x = np.gradient(height)
    normals = np.stack([-grad_x * width / 4.0, grad_y_down * width / 4.0, np.ones_like(height)], axis=-1)
    return normalize(normals), distance


def compute_normals(pipe, rgba, job):
    height, width = rgba.shape[:2]
    mask = rgba[..., 3] / 255.0 >= ALPHA_THRESHOLD
    if not mask.any():
        flat = np.zeros((height, width, 3), dtype=np.float32)
        flat[..., 2] = 1.0
        return flat

    image, box = prepare_input(rgba, job["resolution"])
    prediction = predict_normals(pipe, image, job["ensembleSize"], job["steps"])
    prediction = np.ascontiguousarray(prediction[box[1]:box[3], box[0]:box[2]])
    ai = resize_normals(prediction, width, height)

    ai[..., 0] *= MODEL_AXIS_SIGNS[0]
    ai[..., 1] *= MODEL_AXIS_SIGNS[1]
    ai[..., 2] = np.abs(ai[..., 2])

    gain = job["normalGain"]
    if gain != 1.0:
        ai[..., :2] *= gain
        ai = normalize(ai)

    edge_width = job["edgeWidth"]
    bevel, distance = bevel_normals(mask, edge_width, job["edgeStrength"])
    weight = np.clip(1.0 - distance / edge_width, 0.0, 1.0)[..., None] * job["edgeBlend"]
    return normalize(ai * (1.0 - weight) + bevel * weight)


def encode(normals, mask):
    encoded = ((normals * 0.5 + 0.5) * 255.0).round().clip(0, 255).astype(np.uint8)
    encoded[~mask] = (128, 128, 255)
    return encoded


def parse_atlas(path):
    """Spine atlas: {page: [region]}, region w/h is the unrotated size."""
    pages = {}
    page = None
    region = None

    for raw in Path(path).read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line:
            page = None
            region = None
            continue
        if page is None:
            page = line
            pages[page] = []
            continue
        if ":" not in line:
            region = {"name": line, "rotate": False}
            pages[page].append(region)
            continue

        key, value = [part.strip() for part in line.split(":", 1)]
        if region is None:
            continue
        if key == "rotate":
            region["rotate"] = value in ("true", "90")
        elif key == "xy":
            region["x"], region["y"] = [int(v) for v in value.split(",")]
        elif key == "size":
            region["w"], region["h"] = [int(v) for v in value.split(",")]
        elif key == "bounds":
            region["x"], region["y"], region["w"], region["h"] = [int(v) for v in value.split(",")]

    return pages


def process_sprite(pipe, job):
    rgba = np.asarray(Image.open(job["source"]).convert("RGBA"), dtype=np.float32)
    mask = rgba[..., 3] / 255.0 >= ALPHA_THRESHOLD
    return encode(compute_normals(pipe, rgba, job), mask)


def process_atlas_page(pipe, job):
    page_name = Path(job["source"]).name
    regions = parse_atlas(job["atlas"]).get(page_name)
    if regions is None:
        raise ValueError(f"page '{page_name}' is not found in atlas '{job['atlas']}'")

    page = np.asarray(Image.open(job["source"]).convert("RGBA"), dtype=np.float32)
    mask = page[..., 3] / 255.0 >= ALPHA_THRESHOLD
    normals = np.zeros(page.shape[:2] + (3,), dtype=np.float32)
    normals[..., 2] = 1.0

    for region in regions:
        x, y = region["x"], region["y"]
        packed_w, packed_h = (region["h"], region["w"]) if region["rotate"] else (region["w"], region["h"])
        crop = page[y:y + packed_h, x:x + packed_w]
        upright = np.rot90(crop, -1) if region["rotate"] else crop

        print(f"[info] region {region['name']}", flush=True)
        region_normals = compute_normals(pipe, np.ascontiguousarray(upright), job)
        if region["rotate"]:
            region_normals = np.rot90(region_normals, 1)

        normals[y:y + packed_h, x:x + packed_w] = region_normals

    return encode(normals, mask)


def main():
    if len(sys.argv) != 2:
        print("ERROR usage: sprite_normals.py <jobs.json>", flush=True)
        return 2

    config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    jobs = config["jobs"]
    threads = int(config.get("threads", 4))
    configure_cpu(threads)

    pipe = load_pipeline(config["model"], pick_device(threads))
    failed = 0

    for index, job in enumerate(jobs):
        name = Path(job["source"]).name
        print(f"PROGRESS {index} {len(jobs)} {name}", flush=True)
        try:
            encoded = process_atlas_page(pipe, job) if job.get("atlas") else process_sprite(pipe, job)
            Image.fromarray(encoded).save(job["output"])
            print(f"DONE {job['output']}", flush=True)
        except Exception as exception:
            failed += 1
            print(f"ERROR {name}: {exception}", flush=True)
            traceback.print_exc()

    print(f"PROGRESS {len(jobs)} {len(jobs)} finished", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
