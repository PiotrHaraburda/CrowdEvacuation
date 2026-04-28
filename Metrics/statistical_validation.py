import numpy as np
import pandas as pd
from pathlib import Path
from scipy import stats
from statsmodels.stats.multitest import multipletests

ROOT = Path(__file__).parent

SCENARIOS = {
    "NarrowDoor": ("NarrowDoor", "LC_001", True),
    "Crossing90": ("Crossing90", "D_003", True),
    "Building": ("Building", "Building", False),
}

SCALAR_METRICS = [
    "rset_100s", "rset_95s", "rset_90s", "rset_50s",
    "throughput_ps", "mean_speed_ms",
    "mean_exit_density_pm2", "peak_exit_density_pm2",
    "specific_flow_mean_pms", "specific_flow_peak_pms",
    "mean_headway_m", "min_headway_m",
    "evacuation_rate",
]

DIAGNOSTIC_METRICS = ["collisions_agent_agent", "collisions_agent_wall"]

N_RUNS = 20


def load_summary(folder):
    df = pd.read_csv(folder / "summary.csv", sep=";", decimal=",")
    df["value"] = pd.to_numeric(df["value"].astype(str).str.replace(",", "."), errors="coerce")
    df = df.dropna(subset=["value"])
    return dict(zip(df["metric"], df["value"]))


def load_csv(folder, fname):
    return pd.read_csv(folder / fname, sep=";", decimal=",")


def run_folders(base, prefix, model, n):
    return [base / f"{prefix}_{model}_run{i}" for i in range(1, n + 1)
            if (base / f"{prefix}_{model}_run{i}").exists()]


def pool_column(folders, fname, column):
    return np.concatenate([load_csv(f, fname)[column].dropna().values for f in folders])


def cohen_d(a, b):
    pooled = np.sqrt((a.var(ddof=1) + b.var(ddof=1)) / 2)
    return (a.mean() - b.mean()) / pooled


def rmse(a, b):
    mask = ~(np.isnan(a) | np.isnan(b))
    return np.sqrt(np.mean((a[mask] - b[mask]) ** 2))


def binned_fd(density, speed, bins):
    idx = np.digitize(density, bins) - 1
    return np.array([speed[idx == b].mean() if (idx == b).any() else np.nan
                     for b in range(len(bins) - 1)])


def diagnostic_metric(sfm, rl, metric):
    return {
        "metric": metric,
        "SFM_mean": sfm.mean(), "SFM_sd": sfm.std(ddof=1),
        "RL_mean": rl.mean(), "RL_sd": rl.std(ddof=1),
        "RL_to_SFM_ratio": rl.mean() / sfm.mean() if sfm.mean() else np.nan,
    }


def compare_scalar(sfm, rl, ghost, metric):
    _, p = stats.mannwhitneyu(sfm, rl, alternative="two-sided")
    r = {
        "metric": metric,
        "SFM_mean": sfm.mean(), "SFM_sd": sfm.std(ddof=1),
        "RL_mean": rl.mean(), "RL_sd": rl.std(ddof=1),
        "Ghost": ghost, "MW_p": p, "cohen_d": cohen_d(sfm, rl),
    }
    if not np.isnan(ghost) and ghost != 0:
        r["SFM_err"] = (sfm.mean() - ghost) / ghost
        r["RL_err"] = (rl.mean() - ghost) / ghost
    else:
        r["SFM_err"] = r["RL_err"] = np.nan
    return r


def compare_distribution(sfm, rl, ghost, name):
    d_sr, p_sr = stats.ks_2samp(sfm, rl)
    r = {
        "distribution": name,
        "n_SFM": len(sfm), "n_RL": len(rl), "n_Ghost": len(ghost),
        "SFM_mean": sfm.mean(), "SFM_sd": sfm.std(ddof=1),
        "RL_mean": rl.mean(), "RL_sd": rl.std(ddof=1),
        "SFMvsRL_KS_D": d_sr, "SFMvsRL_KS_p": p_sr,
        "SFMvsRL_W1": stats.wasserstein_distance(sfm, rl),
    }
    if len(ghost):
        d_sg, p_sg = stats.ks_2samp(sfm, ghost)
        d_rg, p_rg = stats.ks_2samp(rl, ghost)
        r["SFMvsGhost_KS_D"], r["SFMvsGhost_KS_p"] = d_sg, p_sg
        r["RLvsGhost_KS_D"], r["RLvsGhost_KS_p"] = d_rg, p_rg
    return r


def compare_fd(sfm_folders, rl_folders, ghost_folder):
    bins = np.arange(0, 6, 0.25)

    def fd_for(folders):
        return binned_fd(
            pool_column(folders, "fundamental_diagram.csv", "density"),
            pool_column(folders, "fundamental_diagram.csv", "speed"),
            bins,
        )

    sfm_fd, rl_fd = fd_for(sfm_folders), fd_for(rl_folders)
    result = {"SFMvsRL_RMSE": rmse(sfm_fd, rl_fd)}

    if ghost_folder:
        gdf = load_csv(ghost_folder, "fundamental_diagram.csv")
        ghost_fd = binned_fd(gdf["density"].values, gdf["speed"].values, bins)
        result["SFMvsGhost_RMSE"] = rmse(sfm_fd, ghost_fd)
        result["RLvsGhost_RMSE"] = rmse(rl_fd, ghost_fd)

    return result


def compare_evac_curves(sfm_folders, rl_folders, ghost_folder):
    t_common = np.linspace(0, 300, 600)

    def load_curves(folders):
        return np.array([
            np.interp(t_common, df["time"], df["evacuated_count"],
                      left=0, right=df["evacuated_count"].iloc[-1])
            for f in folders for df in [load_csv(f, "evacuation_curve.csv")]
        ])

    sfm_arr, rl_arr = load_curves(sfm_folders), load_curves(rl_folders)
    half = sfm_arr.shape[1] // 2
    sfm_mean, rl_mean = sfm_arr.mean(axis=0), rl_arr.mean(axis=0)

    r = {
        "SFM_final_mean": sfm_arr[:, -1].mean(), "SFM_final_sd": sfm_arr[:, -1].std(ddof=1),
        "SFM_half_mean": sfm_arr[:, half].mean(),
        "RL_final_mean": rl_arr[:, -1].mean(), "RL_final_sd": rl_arr[:, -1].std(ddof=1),
        "RL_half_mean": rl_arr[:, half].mean(),
        "SFMvsRL_curve_RMSE": np.sqrt(np.mean((sfm_mean - rl_mean) ** 2)),
    }

    if ghost_folder:
        gdf = load_csv(ghost_folder, "evacuation_curve.csv")
        ghost_curve = np.interp(t_common, gdf["time"], gdf["evacuated_count"],
                                left=0, right=gdf["evacuated_count"].iloc[-1])
        r["SFMvsGhost_curve_RMSE"] = np.sqrt(np.mean((sfm_mean - ghost_curve) ** 2))
        r["RLvsGhost_curve_RMSE"] = np.sqrt(np.mean((rl_mean - ghost_curve) ** 2))

    return r


def process(scen, folder, prefix, has_ghost, out_dir):
    print(f"\n{scen}")
    base = ROOT / folder
    sfm_folders = run_folders(base, prefix, "SFM", N_RUNS)
    rl_folders = run_folders(base, prefix, "RL", N_RUNS)
    ghost_folder = base / prefix if has_ghost else None

    print(f"  SFM: {len(sfm_folders)}, RL: {len(rl_folders)}, Ghost: {'yes' if ghost_folder else 'no'}")

    sfm_sums = [load_summary(f) for f in sfm_folders]
    rl_sums = [load_summary(f) for f in rl_folders]
    ghost_sum = load_summary(ghost_folder) if ghost_folder else {}

    def col(sums, m):
        return np.array([s[m] for s in sums])

    scalar_df = pd.DataFrame([
        compare_scalar(col(sfm_sums, m), col(rl_sums, m), ghost_sum.get(m, np.nan), m)
        for m in SCALAR_METRICS
    ])
    _, p_adj, _, _ = multipletests(scalar_df["MW_p"].values, alpha=0.05, method="holm")
    scalar_df["MW_p_holm"] = p_adj
    scalar_df["sig"] = p_adj < 0.05

    diag_df = pd.DataFrame([
        diagnostic_metric(col(sfm_sums, m), col(rl_sums, m), m)
        for m in DIAGNOSTIC_METRICS
    ])

    dist_df = pd.DataFrame([
        compare_distribution(
            pool_column(sfm_folders, fname, c),
            pool_column(rl_folders, fname, c),
            pool_column([ghost_folder], fname, c) if ghost_folder else np.array([]),
            name,
        )
        for name, fname, c in [
            ("headway_dist", "headway.csv", "headwayDistance"),
            ("agent_speed", "agents_frame.csv", "speed"),
        ]
    ])

    fd_curves_df = pd.DataFrame([{
        **compare_fd(sfm_folders, rl_folders, ghost_folder),
        **compare_evac_curves(sfm_folders, rl_folders, ghost_folder),
    }])

    scen_dir = out_dir / scen
    scen_dir.mkdir(parents=True, exist_ok=True)
    for df, name in [(scalar_df, "scalar"), (diag_df, "diagnostics"),
                     (dist_df, "distributions"), (fd_curves_df, "fd_and_curves")]:
        df.to_csv(scen_dir / f"{name}.csv", sep=";", decimal=",", index=False, float_format="%.4f")

    print(f"  saved here: {scen_dir}")
    print("\n  Scalars:")
    print(scalar_df[["metric", "SFM_mean", "SFM_sd", "RL_mean", "RL_sd",
                     "Ghost", "MW_p", "cohen_d", "MW_p_holm"]].to_string(index=False))
    print("\n  Diagnostics:")
    print(diag_df.to_string(index=False))
    print("\n  Distributions (KS + Wasserstein):")
    print(dist_df.to_string(index=False))
    print("\n  FD + evacuation curves:")
    print(fd_curves_df.to_string(index=False))


def main():
    out_dir = ROOT / "StatisticalAnalysis"
    for scen, (folder, prefix, has_ghost) in SCENARIOS.items():
        process(scen, folder, prefix, has_ghost, out_dir)


if __name__ == "__main__":
    main()