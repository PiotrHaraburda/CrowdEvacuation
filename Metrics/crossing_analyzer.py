import matplotlib.gridspec as gridspec
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

plt.rcParams['figure.dpi'] = 150
plt.rcParams['font.size'] = 9
plt.rcParams['axes.titlesize'] = 11
plt.rcParams['axes.labelsize'] = 9

base = './Crossing90'
C_GHOST = '#2196F3'
C_SFM = '#FF5722'
C_RL = '#4CAF50'

models = [
    ('Ghost', C_GHOST, 'D_007'),
    ('SFM', C_SFM, 'D_007_SFM_cal_20'),
    ('RL', C_RL, 'D_007_RL'),
]


def load_csv(path):
    return pd.read_csv(path, sep=';', decimal=',')


def load_model(run_id):
    return {
        'ec': load_csv(f'{base}/{run_id}/evacuation_curve.csv'),
        'fd': load_csv(f'{base}/{run_id}/fundamental_diagram.csv'),
        'sf': load_csv(f'{base}/{run_id}/specific_flow_series.csv'),
        'hm': load_csv(f'{base}/{run_id}/density_heatmap.csv'),
        'af': load_csv(f'{base}/{run_id}/agents_frame.csv'),
        'hw': load_csv(f'{base}/{run_id}/headway.csv'),
        'tp': load_csv(f'{base}/{run_id}/throughput.csv'),
        'co': load_csv(f'{base}/{run_id}/collisions.csv'),
        'sum': load_csv(f'{base}/{run_id}/summary.csv'),
    }


data = {name: load_model(run_id) for name, _, run_id in models}
colors = {name: color for name, color, _ in models}

fig = plt.figure(figsize=(20, 28))
gs = gridspec.GridSpec(6, 3, hspace=0.4, wspace=0.35)

# (a) Evacuation Curve
ax = fig.add_subplot(gs[0, 0])
for name in data:
    ax.plot(data[name]['ec']['time'], data[name]['ec']['evacuated_count'],
            color=colors[name], linewidth=2, label=name)
total = max(d['ec']['evacuated_count'].max() for d in data.values())
ax.axhline(y=total * 0.5, color='gray', ls=':', alpha=0.5, label='50%')
ax.axhline(y=total * 0.95, color='gray', ls='--', alpha=0.5, label='95%')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Evacuated agents')
ax.set_title('(a) Evacuation Curve')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)
ax.set_ylim(0)

# (b) Throughput timeline
ax = fig.add_subplot(gs[0, 1])
for name in data:
    tp = data[name]['tp'].sort_values('time')
    if len(tp) > 1:
        intervals = tp['time'].diff().dropna()
        roll = intervals.rolling(window=10, center=True).mean()
        ax.plot(tp['time'].iloc[1:], roll, color=colors[name], lw=1.2, alpha=0.8,
                label=f'{name} (10-agent avg)')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Inter-exit interval [s]')
ax.set_title('(b) Time Between Exits')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (c) Summary table
ax = fig.add_subplot(gs[0, 2])
ax.axis('off')


def fmt(v):
    try:
        return f"{float(str(v).replace(',', '.')):.2f}"
    except (ValueError, TypeError):
        return str(v)


metrics_show = [
    ('RSET 100%', 'rset_100s', 's'),
    ('RSET 95%', 'rset_95s', 's'),
    ('RSET 50%', 'rset_50s', 's'),
    ('Throughput', 'throughput_ps', 'p/s'),
    ('Mean speed', 'mean_speed_ms', 'm/s'),
    ('Peak exit dens.', 'peak_exit_density_pm2', 'p/m²'),
    ('Min headway', 'min_headway_m', 'm'),
    ('Mean headway', 'mean_headway_m', 'm'),
]

table_data = []
for label, key, unit in metrics_show:
    row = [label]
    for name in data:
        d = dict(zip(data[name]['sum']['metric'], data[name]['sum']['value']))
        row.append(f"{fmt(d.get(key, 'N/A'))} {unit}")
    table_data.append(row)

col_labels = ['Metric'] + list(data.keys())
table = ax.table(cellText=table_data, colLabels=col_labels, cellLoc='center', loc='center',
                 colWidths=[0.30] + [0.22] * len(data))
table.auto_set_font_size(False)
table.set_fontsize(7)
table.scale(1, 1.3)
for j in range(len(col_labels)):
    table[0, j].set_facecolor('#E0E0E0')
    table[0, j].set_text_props(fontweight='bold')
ax.set_title('(c) Summary', pad=15)

# (d) Fundamental Diagram v(ρ)
ax = fig.add_subplot(gs[1, 0])
for name in data:
    fd = data[name]['fd']
    fd = fd[(fd['density'] > 0) & (fd['density'] < 8) & (fd['speed'] >= 0) & (fd['speed'] < 3)]
    bins = np.arange(0.25, min(fd['density'].max() + 0.25, 6), 0.25)
    if len(bins) > 1:
        fd_c = fd.copy()
        fd_c['bin'] = pd.cut(fd_c['density'], bins)
        means = fd_c.groupby('bin', observed=False)['speed'].agg(['mean', 'std']).dropna()
        centers = [(b.left + b.right) / 2 for b in means.index]
        ax.plot(centers, means['mean'].values, color=colors[name], lw=2.5,
                marker='o', ms=3, label=name)
        ax.fill_between(centers, (means['mean'] - means['std']).values,
                        (means['mean'] + means['std']).values, color=colors[name], alpha=0.15)
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Speed [m/s]')
ax.set_title('(d) Fundamental Diagram v(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

# (e) Speed distribution
ax = fig.add_subplot(gs[1, 1])
for name in data:
    sp = data[name]['af']['speed']
    sp = sp[(sp < 3) & (sp > 0.01)]
    sp.plot.kde(ax=ax, color=colors[name], linewidth=2, label=f'{name} (μ={sp.mean():.2f})')
    ax.axvline(sp.mean(), color=colors[name], linestyle='--', linewidth=1, alpha=0.7)
ax.set_xlabel('Speed [m/s]')
ax.set_ylabel('Probability density')
ax.set_title('(e) Speed Distribution')
ax.set_xlim(0, 2.5)
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (f) Headway distribution
ax = fig.add_subplot(gs[1, 2])
for name in data:
    hw = data[name]['hw']['headwayDistance']
    hw = hw[hw < 2.5]
    hw.plot.kde(ax=ax, color=colors[name], linewidth=2, label=f'{name} (μ={hw.mean():.2f}m)')
    ax.axvline(hw.mean(), color=colors[name], linestyle='--', linewidth=1, alpha=0.7)
ax.set_xlabel('Headway [m]')
ax.set_ylabel('Probability density')
ax.set_title('(f) Headway Distribution')
ax.set_xlim(0, 2.0)
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (g) Specific flow
ax = fig.add_subplot(gs[2, 0])
for name in data:
    sf = data[name]['sf']
    sf = sf[sf['specific_flow'] < 10]
    w = max(1, len(sf) // 60)
    sm = sf['specific_flow'].rolling(window=w, center=True).mean()
    ax.fill_between(sf['time'], 0, sf['specific_flow'], color=colors[name], alpha=0.06)
    ax.plot(sf['time'], sm, color=colors[name], lw=1.2, label=name)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Specific flow [ped/m/s]')
ax.set_title('(g) Specific Flow at Exit')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (h) Mean speed over time
ax = fig.add_subplot(gs[2, 1])
for name in data:
    af = data[name]['af']
    af = af[af['speed'] < 3]
    ts = af.groupby(af['time'].round(1))['speed'].mean()
    w = max(1, len(ts) // 40)
    sm = ts.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=colors[name], lw=1.5, label=name)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Mean speed [m/s]')
ax.set_title('(h) Mean Speed Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (i) Active agents
ax = fig.add_subplot(gs[2, 2])
for name in data:
    af = data[name]['af']
    ac = af.groupby(af['time'].round(1))['agentId'].nunique()
    w = max(1, len(ac) // 40)
    sm = ac.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=colors[name], lw=1.5, label=name)
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
    im = ax.imshow(pv, cmap='YlOrRd', aspect='equal', interpolation='nearest', vmin=vmin, vmax=vmax)
    ax.set_xticks(range(len(pv.columns)))
    ax.set_xticklabels(pv.columns, fontsize=6)
    ax.set_yticks(range(len(pv.index)))
    ax.set_yticklabels(pv.index, fontsize=6)
    ax.set_xlabel('X [m]')
    ax.set_ylabel('Z [m]')
    ax.set_title(title)
    plt.colorbar(im, ax=ax, label='ped/m²', shrink=0.7)
    return pv


vmax = max(d['hm'].groupby(['grid_x', 'grid_z'])['density'].mean().max() for d in data.values())
hm_pvs = {}
for i, name in enumerate(data):
    hm_pvs[name] = make_heatmap(fig.add_subplot(gs[3, i]), data[name]['hm'],
                                 f'({chr(106 + i)}) Density - {name}', 0, vmax)

# (m,n) Heatmap differences
ax = fig.add_subplot(gs[4, 0])
gpv = hm_pvs['Ghost']
spv = hm_pvs['SFM']
ci = gpv.index.intersection(spv.index)
cc = gpv.columns.intersection(spv.columns)
if len(ci) > 0 and len(cc) > 0:
    diff = spv.reindex(index=ci, columns=cc).fillna(0) - gpv.reindex(index=ci, columns=cc).fillna(0)
    vabs = max(abs(diff.min().min()), abs(diff.max().max()), 0.01)
    im = ax.imshow(diff, cmap='RdBu_r', aspect='equal', interpolation='nearest', vmin=-vabs, vmax=vabs)
    ax.set_xticks(range(len(diff.columns)))
    ax.set_xticklabels(diff.columns, fontsize=6)
    ax.set_yticks(range(len(diff.index)))
    ax.set_yticklabels(diff.index, fontsize=6)
    ax.set_xlabel('X [m]')
    ax.set_ylabel('Z [m]')
    ax.set_title('(m) Difference (SFM − Ghost)')
    plt.colorbar(im, ax=ax, label='Δ ped/m²', shrink=0.7)

ax = fig.add_subplot(gs[4, 1])
rpv = hm_pvs['RL']
ci = gpv.index.intersection(rpv.index)
cc = gpv.columns.intersection(rpv.columns)
if len(ci) > 0 and len(cc) > 0:
    diff = rpv.reindex(index=ci, columns=cc).fillna(0) - gpv.reindex(index=ci, columns=cc).fillna(0)
    vabs = max(abs(diff.min().min()), abs(diff.max().max()), 0.01)
    im = ax.imshow(diff, cmap='RdBu_r', aspect='equal', interpolation='nearest', vmin=-vabs, vmax=vabs)
    ax.set_xticks(range(len(diff.columns)))
    ax.set_xticklabels(diff.columns, fontsize=6)
    ax.set_yticks(range(len(diff.index)))
    ax.set_yticklabels(diff.index, fontsize=6)
    ax.set_xlabel('X [m]')
    ax.set_ylabel('Z [m]')
    ax.set_title('(n) Difference (RL − Ghost)')
    plt.colorbar(im, ax=ax, label='Δ ped/m²', shrink=0.7)

# (o) Headway over time
ax = fig.add_subplot(gs[4, 2])
for name in data:
    hw = data[name]['hw']
    th = hw.groupby(hw['time'].round(1))['headwayDistance'].mean()
    w = max(1, len(th) // 25)
    sm = th.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=colors[name], lw=1.5, label=name)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Mean headway [m]')
ax.set_title('(o) Mean Headway Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)

# (p) Flow-Density
ax = fig.add_subplot(gs[5, 0])
for name in data:
    fd = data[name]['fd']
    fd = fd[(fd['density'] > 0) & (fd['density'] < 6) & (fd['speed'] < 3) & (fd['speed'] >= 0)]
    bins = np.arange(0.25, min(fd['density'].max() + 0.25, 6), 0.25)
    if len(bins) > 1:
        fd_c = fd.copy()
        fd_c['flow'] = fd_c['density'] * fd_c['speed']
        fd_c['bin'] = pd.cut(fd_c['density'], bins)
        means = fd_c.groupby('bin', observed=False)['flow'].mean().dropna()
        centers = [(b.left + b.right) / 2 for b in means.index]
        ax.plot(centers, means.values, color=colors[name], lw=2.5,
                marker='o', ms=3, label=f'{name} (mean)')
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Flow [ped/m²/s]')
ax.set_title('(p) Flow–Density J(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

plt.suptitle('Crossing90 D_007 (entrance 2.4m) - Ghost vs SFM vs RL',
             fontsize=14, fontweight='bold', y=1.0)
plt.savefig('./Charts/crossing90_d007_comparison.png', dpi=150, bbox_inches='tight')
print("\nDone")
