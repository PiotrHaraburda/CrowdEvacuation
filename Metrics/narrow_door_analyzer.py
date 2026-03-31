import matplotlib.gridspec as gridspec
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

plt.rcParams['figure.dpi'] = 150
plt.rcParams['font.size'] = 9
plt.rcParams['axes.titlesize'] = 11
plt.rcParams['axes.labelsize'] = 9

base = './NarrowDoor'
C_GHOST = '#2196F3'
C_SFM = '#FF5722'
C_GHOST_LIGHT = '#90CAF9'
C_SFM_LIGHT = '#FFAB91'


def load_csv(path):
    return pd.read_csv(path, sep=';', decimal=',')


ghost = {
    'ec': load_csv(f'{base}/LC_001/evacuation_curve.csv'),
    'fd': load_csv(f'{base}/LC_001/fundamental_diagram.csv'),
    'sf': load_csv(f'{base}/LC_001/specific_flow_series.csv'),
    'hm': load_csv(f'{base}/LC_001/density_heatmap.csv'),
    'af': load_csv(f'{base}/LC_001/agents_frame.csv'),
    'hw': load_csv(f'{base}/LC_001/headway.csv'),
    'tp': load_csv(f'{base}/LC_001/throughput.csv'),
    'co': load_csv(f'{base}/LC_001/collisions.csv'),
    'sum': load_csv(f'{base}/LC_001/summary.csv'),
}
sfm = {
    'ec': load_csv(f'{base}/LC_001_SFM_cal_09/evacuation_curve.csv'),
    'fd': load_csv(f'{base}/LC_001_SFM_cal_09/fundamental_diagram.csv'),
    'sf': load_csv(f'{base}/LC_001_SFM_cal_09/specific_flow_series.csv'),
    'hm': load_csv(f'{base}/LC_001_SFM_cal_09/density_heatmap.csv'),
    'af': load_csv(f'{base}/LC_001_SFM_cal_09/agents_frame.csv'),
    'hw': load_csv(f'{base}/LC_001_SFM_cal_09/headway.csv'),
    'tp': load_csv(f'{base}/LC_001_SFM_cal_09/throughput.csv'),
    'co': load_csv(f'{base}/LC_001_SFM_cal_09/collisions.csv'),
    'sum': load_csv(f'{base}/LC_001_SFM_cal_09/summary.csv'),
}

fig = plt.figure(figsize=(20, 24))
gs = gridspec.GridSpec(5, 3, hspace=0.4, wspace=0.35)

# (a) Evacuation Curve
ax = fig.add_subplot(gs[0, 0])
ax.plot(ghost['ec']['time'], ghost['ec']['evacuated_count'], color=C_GHOST, linewidth=2, label='Ghost (empirical)')
ax.plot(sfm['ec']['time'], sfm['ec']['evacuated_count'], color=C_SFM, linewidth=2, label='SFM (Helbing 2000)')
ax.axhline(y=93 * 0.5, color='gray', linestyle=':', alpha=0.5, label='50% evacuated')
ax.axhline(y=93 * 0.95, color='gray', linestyle='--', alpha=0.5, label='95% evacuated')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Evacuated agents')
ax.set_title('(a) Evacuation Curve')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)
ax.set_ylim(0, 96)

# (b) Throughput timeline
ax = fig.add_subplot(gs[0, 1])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    tp = data['tp'].sort_values('time')
    if len(tp) > 1:
        intervals = tp['time'].diff().dropna()
        roll = intervals.rolling(window=5, center=True).mean()
        ax.plot(tp['time'].iloc[1:], roll, color=color, linewidth=1.5, alpha=0.8, label=f'{label} (5-agent avg)')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Inter-exit interval [s]')
ax.set_title('(b) Time Between Consecutive Exits')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (c) Summary table
ax = fig.add_subplot(gs[0, 2])
ax.axis('off')
ghost_dict = dict(zip(ghost['sum']['metric'], ghost['sum']['value']))
sfm_dict = dict(zip(sfm['sum']['metric'], sfm['sum']['value']))


def fmt(v):
    try:
        return f"{float(str(v).replace(',', '.')):.2f}"
    except:
        return str(v)


metrics_show = [
    ('RSET 100%', 'rset_100s', 's'),
    ('RSET 95%', 'rset_95s', 's'),
    ('RSET 50%', 'rset_50s', 's'),
    ('Throughput', 'throughput_ps', 'p/s'),
    ('Mean speed', 'mean_speed_ms', 'm/s'),
    ('Peak exit density', 'peak_exit_density_pm2', 'p/m²'),
    ('Min headway', 'min_headway_m', 'm'),
    ('Mean headway', 'mean_headway_m', 'm'),
    ('Collisions (A-A)', 'collisions_agent_agent', ''),
    ('Collisions (A-W)', 'collisions_agent_wall', ''),
]

table_data = []
for label, key, unit in metrics_show:
    gv = fmt(ghost_dict.get(key, 'N/A'))
    sv = fmt(sfm_dict.get(key, 'N/A'))
    table_data.append([label, f"{gv} {unit}", f"{sv} {unit}"])

table = ax.table(cellText=table_data,
                 colLabels=['Metric', 'Ghost', 'SFM'],
                 cellLoc='center', loc='center',
                 colWidths=[0.38, 0.28, 0.28])
table.auto_set_font_size(False)
table.set_fontsize(8)
table.scale(1, 1.3)
for j in range(3):
    table[0, j].set_facecolor('#E0E0E0')
    table[0, j].set_text_props(fontweight='bold')
ax.set_title('(c) Summary', pad=15)

# (d) Fundamental Diagram v(ρ)
ax = fig.add_subplot(gs[1, 0])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    fd = data['fd']
    fd = fd[(fd['density'] > 0) & (fd['density'] < 10) & (fd['speed'] < 3) & (fd['speed'] >= 0)]
    ax.scatter(fd['density'], fd['speed'], c=color, s=2, alpha=0.15)
    # Binned averages
    bins = np.arange(0.5, min(fd['density'].max() + 0.5, 10), 0.5)
    if len(bins) > 1:
        fd_c = fd.copy()
        fd_c['bin'] = pd.cut(fd_c['density'], bins)
        means = fd_c.groupby('bin')['speed'].agg(['mean', 'std']).dropna()
        centers = [(b.left + b.right) / 2 for b in means.index]
        ax.plot(centers, means['mean'].values, color=color, linewidth=2.5,
                marker='o', markersize=4, label=f'{label} (mean)')
        ax.fill_between(centers,
                        (means['mean'] - means['std']).values,
                        (means['mean'] + means['std']).values,
                        color=color, alpha=0.1)
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Speed [m/s]')
ax.set_title('(d) Fundamental Diagram v(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

# (e) Speed distribution
ax = fig.add_subplot(gs[1, 1])
ghost_speeds = ghost['af']['speed']
ghost_speeds = ghost_speeds[ghost_speeds < 3]
sfm_speeds = sfm['af']['speed']
sfm_speeds = sfm_speeds[sfm_speeds < 3]
bins_speed = np.arange(0, 3.1, 0.1)
ax.hist(ghost_speeds, bins=bins_speed, color=C_GHOST, alpha=0.5, density=True, label='Ghost')
ax.hist(sfm_speeds, bins=bins_speed, color=C_SFM, alpha=0.5, density=True, label='SFM')
ax.axvline(ghost_speeds.mean(), color=C_GHOST, linestyle='--', linewidth=1.5,
           label=f'Ghost μ={ghost_speeds.mean():.2f}')
ax.axvline(sfm_speeds.mean(), color=C_SFM, linestyle='--', linewidth=1.5, label=f'SFM μ={sfm_speeds.mean():.2f}')
ax.set_xlabel('Speed [m/s]')
ax.set_ylabel('Density')
ax.set_title('(e) Speed Distribution')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (f) Headway distribution
ax = fig.add_subplot(gs[1, 2])
ghost_hw = ghost['hw']['headwayDistance']
sfm_hw = sfm['hw']['headwayDistance']
bins_hw = np.arange(0, 2.0, 0.05)
ax.hist(ghost_hw, bins=bins_hw, color=C_GHOST, alpha=0.5, density=True, label='Ghost')
ax.hist(sfm_hw, bins=bins_hw, color=C_SFM, alpha=0.5, density=True, label='SFM')
ax.axvline(ghost_hw.mean(), color=C_GHOST, linestyle='--', linewidth=1.5, label=f'Ghost μ={ghost_hw.mean():.2f}m')
ax.axvline(sfm_hw.mean(), color=C_SFM, linestyle='--', linewidth=1.5, label=f'SFM μ={sfm_hw.mean():.2f}m')
ax.set_xlabel('Headway distance [m]')
ax.set_ylabel('Density')
ax.set_title('(f) Headway Distribution')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (g) Specific flow
ax = fig.add_subplot(gs[2, 0])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    sf = data['sf']
    w = max(1, len(sf) // 40)
    smooth = sf['specific_flow'].rolling(window=w, center=True).mean()
    ax.fill_between(sf['time'], 0, sf['specific_flow'], color=color, alpha=0.1)
    ax.plot(sf['time'], smooth, color=color, linewidth=1.5, label=f'{label}')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Specific flow [ped/m/s]')
ax.set_title('(g) Specific Flow at Exit')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (h) Mean speed over time
ax = fig.add_subplot(gs[2, 1])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    af = data['af']
    af_filtered = af[af['speed'] < 4.0]
    time_speed = af_filtered.groupby(af_filtered['time'].round(1))['speed'].mean()
    w = max(1, len(time_speed) // 30)
    smooth = time_speed.rolling(window=w, center=True).mean()
    ax.plot(smooth.index, smooth.values, color=color, linewidth=1.5, label=label)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Mean speed [m/s]')
ax.set_title('(h) Mean Speed Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (i) Active agents
ax = fig.add_subplot(gs[2, 2])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    af = data['af']
    active_count = af.groupby(af['time'].round(1))['agentId'].nunique()
    w = max(1, len(active_count) // 30)
    smooth = active_count.rolling(window=w, center=True).mean()
    ax.plot(smooth.index, smooth.values, color=color, linewidth=1.5, label=label)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Active agents')
ax.set_title('(i) Active Agents Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)


# (j,k,l) Density heatmaps
def make_heatmap(ax, df, title, vmin, vmax):
    avg = df.groupby(['grid_x', 'grid_z'])['density'].mean().reset_index()
    pv = avg.pivot(index='grid_z', columns='grid_x', values='density').sort_index(ascending=False)
    im = ax.imshow(pv, cmap='YlOrRd', aspect='auto', interpolation='nearest', vmin=vmin, vmax=vmax)
    ax.set_xticks(range(len(pv.columns)))
    ax.set_xticklabels(pv.columns, fontsize=6)
    ax.set_yticks(range(len(pv.index)))
    ax.set_yticklabels(pv.index, fontsize=6)
    ax.set_xlabel('X [m]')
    ax.set_ylabel('Z [m]')
    ax.set_title(title)
    plt.colorbar(im, ax=ax, label='ped/m²', shrink=0.7)
    return pv


vmax_g = ghost['hm'].groupby(['grid_x', 'grid_z'])['density'].mean().max()
vmax_s = sfm['hm'].groupby(['grid_x', 'grid_z'])['density'].mean().max()
vmax = max(vmax_g, vmax_s)

gpv = make_heatmap(fig.add_subplot(gs[3, 0]), ghost['hm'], '(j) Density - Ghost', 0, vmax)
spv = make_heatmap(fig.add_subplot(gs[3, 1]), sfm['hm'], '(k) Density - SFM', 0, vmax)

ax = fig.add_subplot(gs[3, 2])
ci = gpv.index.intersection(spv.index)
cc = gpv.columns.intersection(spv.columns)
if len(ci) > 0 and len(cc) > 0:
    diff = spv.reindex(index=ci, columns=cc).fillna(0) - gpv.reindex(index=ci, columns=cc).fillna(0)
    vabs = max(abs(diff.min().min()), abs(diff.max().max()), 0.01)
    im = ax.imshow(diff, cmap='RdBu_r', aspect='auto', interpolation='nearest', vmin=-vabs, vmax=vabs)
    ax.set_xticks(range(len(diff.columns)))
    ax.set_xticklabels(diff.columns, fontsize=6)
    ax.set_yticks(range(len(diff.index)))
    ax.set_yticklabels(diff.index, fontsize=6)
    ax.set_xlabel('X [m]');
    ax.set_ylabel('Z [m]')
    ax.set_title('(l) Difference (SFM − Ghost)')
    plt.colorbar(im, ax=ax, label='Δ ped/m²', shrink=0.7)

# (m) Collisions
ax = fig.add_subplot(gs[4, 0])
co = sfm['co']
if len(co) > 1:
    bins_t = np.arange(0, co['time'].max() + 2, 2)
    for typ, col, lbl in [('Wall', 'brown', 'Wall'), ('Agent', 'red', 'Agent')]:
        sub = co[co['type'] == typ]
        if len(sub) > 0:
            ax.hist(sub['time'], bins=bins_t, color=col, alpha=0.6, label=f'{lbl} ({len(sub)})')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Count')
ax.set_title('(m) SFM Collisions')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (n) Headway over time
ax = fig.add_subplot(gs[4, 1])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    hw = data['hw']
    time_hw = hw.groupby(hw['time'].round(1))['headwayDistance'].mean()
    w = max(1, len(time_hw) // 20)
    smooth = time_hw.rolling(window=w, center=True).mean()
    ax.plot(smooth.index, smooth.values, color=color, linewidth=1.5, label=label)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Mean headway [m]')
ax.set_title('(n) Mean Headway Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (o) Flow-Density
ax = fig.add_subplot(gs[4, 2])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    fd = data['fd']
    fd = fd[(fd['density'] > 0) & (fd['density'] < 10) & (fd['speed'] < 3) & (fd['speed'] >= 0)]
    flow = fd['density'] * fd['speed']
    ax.scatter(fd['density'], flow, c=color, s=2, alpha=0.15)
    bins = np.arange(0.5, min(fd['density'].max() + 0.5, 10), 0.5)
    if len(bins) > 1:
        fd_c = fd.copy()
        fd_c['flow'] = fd_c['density'] * fd_c['speed']
        fd_c['bin'] = pd.cut(fd_c['density'], bins)
        means = fd_c.groupby('bin')['flow'].mean().dropna()
        centers = [(b.left + b.right) / 2 for b in means.index]
        ax.plot(centers, means.values, color=color, linewidth=2.5,
                marker='o', markersize=4, label=f'{label} (mean)')
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Flow [ped/m²/s]')
ax.set_title('(o) Flow–Density J(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

plt.suptitle('NarrowDoor LC_001 - Ghost (Empirical) vs SFM (Helbing 2000)',
             fontsize=14, fontweight='bold', y=1.0)
plt.savefig('./Charts/narrow_door_calibrated.png', dpi=150, bbox_inches='tight')
print("Done")
