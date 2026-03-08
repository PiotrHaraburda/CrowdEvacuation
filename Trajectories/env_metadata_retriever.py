import json, os
import numpy as np

for name, folder in [("narrow_door", "data_processed/narrow_door"), ("crossing", "data_processed/crossing")]:
    print(f"\n{name}")
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".json"):
            continue
        with open(os.path.join(folder, f)) as fh:
            trajs = json.load(fh)
        if isinstance(trajs, dict):
            trajs = trajs.get("trajectories", trajs)
        all_x = [v for t in trajs for v in t["x"]]
        all_y = [v for t in trajs for v in t["y"]]
        print(f"  {f}: {len(trajs)} agents")
        print(f"    X: [{min(all_x):.3f}, {max(all_x):.3f}]")
        print(f"    Y: [{min(all_y):.3f}, {max(all_y):.3f}]")
        if name == "narrow_door":
            starts_x = [t["x"][0] for t in trajs]
            starts_y = [t["y"][0] for t in trajs]
            ends_x = [t["x"][-1] for t in trajs]
            ends_y = [t["y"][-1] for t in trajs]
            print(f"    Start positions X: [{min(starts_x):.3f}, {max(starts_x):.3f}]")
            print(f"    Start positions Y: [{min(starts_y):.3f}, {max(starts_y):.3f}]")
            print(f"    End positions X: [{min(ends_x):.3f}, {max(ends_x):.3f}]")
            print(f"    End positions Y: [{min(ends_y):.3f}, {max(ends_y):.3f}]")