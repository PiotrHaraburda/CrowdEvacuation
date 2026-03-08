import os, json, glob
import numpy as np

RAW = "data_raw"
OUT = "data_processed"

LC_HOURS = [
    "20131026_094104", "20131026_094344", "20131026_094709",
    "20131026_095007", "20131026_095239", "20131026_095512",
    "20131026_095747", "20131026_100023", "20131026_100303",
    "20131026_100533",
]


def process_narrow_door():
    print("\n=== NARROW DOOR ===")
    nd_raw = os.path.join(RAW, "narrow_door")
    nd_out = os.path.join(OUT, "narrow_door")
    os.makedirs(nd_out, exist_ok=True)

    ep = 0
    for hour in LC_HOURS:
        matches = glob.glob(os.path.join(nd_raw, hour + "*"))
        if not matches:
            print(f"  SKIP: {hour}")
            continue

        folder = matches[0]
        ep += 1
        dats = sorted(glob.glob(os.path.join(folder, "*.dat")))
        if not dats:
            continue

        trajs = []
        for i, dat in enumerate(dats):
            try:
                raw = np.loadtxt(dat)
            except Exception:
                continue
            if raw.ndim != 2 or raw.shape[1] != 3:
                continue
            trajs.append({
                "id": i,
                "x": raw[:, 0].tolist(),
                "y": raw[:, 1].tolist(),
                "frame": raw[:, 2].astype(int).tolist(),
                "t": (raw[:, 2] / 50.0).tolist(),
            })

        fp = os.path.join(nd_out, f"LC_{ep:03d}.json")
        with open(fp, "w") as f:
            json.dump(trajs, f)
        mb = os.path.getsize(fp) / (1024 * 1024)
        print(f"  {os.path.basename(folder)} -> {len(trajs)} agents, {mb:.1f} MB")


def process_crossing():
    if not HAS_H5PY:
        return

    print("\n=== CROSSING 90 ===")
    cr_raw = os.path.join(RAW, "crossing")
    cr_out = os.path.join(OUT, "crossing")
    os.makedirs(cr_out, exist_ok=True)

    h5s = sorted(glob.glob(os.path.join(cr_raw, "crossing_90_d_*.h5")))

    for idx, h5p in enumerate(h5s):
        fname = os.path.basename(h5p)
        fh = h5py.File(h5p, "r")

        traj_ds = None
        def find(name, obj):
            nonlocal traj_ds
            if isinstance(obj, h5py.Dataset) and "traj" in name.lower():
                traj_ds = obj
        fh.visititems(find)

        if traj_ds is None:
            fh.close()
            continue

        raw = traj_ds[:]
        if raw.dtype.names:
            cn = list(raw.dtype.names)
            id_c = next((c for c in cn if "id" in c.lower() or "marker" in c.lower()), cn[0])
            fr_c = next((c for c in cn if "frame" in c.lower()), cn[1])
            x_c = next((c for c in cn if "x" in c.lower()), cn[2])
            y_c = next((c for c in cn if "y" in c.lower()), cn[3])
            ids, frames, xs, ys = raw[id_c], raw[fr_c], raw[x_c].astype(float), raw[y_c].astype(float)
        else:
            ids, frames = raw[:, 0].astype(int), raw[:, 1].astype(int)
            xs, ys = raw[:, 2].astype(float), raw[:, 3].astype(float)

        if np.max(np.abs(xs)) > 50:
            xs /= 100.0
            ys /= 100.0

        fh.close()

        trajs = []
        for aid in np.unique(ids):
            m = ids == aid
            af, ax, ay = frames[m], xs[m], ys[m]
            s = np.argsort(af)
            trajs.append({
                "id": int(aid),
                "x": ax[s].tolist(),
                "y": ay[s].tolist(),
                "frame": af[s].astype(int).tolist(),
                "t": (af[s].astype(float) / 16.0).tolist(),
            })

        fp = os.path.join(cr_out, f"D_{idx+1:03d}.json")
        with open(fp, "w") as fo:
            json.dump(trajs, fo)
        mb = os.path.getsize(fp) / (1024 * 1024)
        print(f"  {fname} -> {len(trajs)} agents, {mb:.1f} MB")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    process_narrow_door()
    process_crossing()