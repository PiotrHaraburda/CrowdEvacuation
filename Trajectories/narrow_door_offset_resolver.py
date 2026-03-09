import json
import numpy as np
import glob

files = sorted(glob.glob("data_processed/narrow_door/LC_*.json"))
all_means = []

for fp in files:
    with open(fp) as f:
        data = json.load(f)

    crossing_x = []
    for agent in data:
        xs = np.array(agent["x"])
        ys = np.array(agent["y"])
        idx = np.where(ys < -1.0)[0]
        if len(idx) > 0:
            crossing_x.append(xs[idx[0]])

    mean = np.mean(crossing_x)
    all_means.append(mean)
    print(f"{fp}: mean_x={mean:.4f}, n={len(crossing_x)}")

print(f"\nGlobal offset: {np.mean(all_means):.4f}")
