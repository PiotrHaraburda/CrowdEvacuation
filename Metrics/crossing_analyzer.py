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


def load_csv(path):
    return pd.read_csv(path, sep=';', decimal=',')


ghost = {
    'ec': load_csv(f'{base}/D_003/evacuation_curve.csv'),
    'fd': load_csv(f'{base}/D_003/fundamental_diagram.csv'),
    'sf': load_csv(f'{base}/D_003/specific_flow_series.csv'),
    'hm': load_csv(f'{base}/D_003/density_heatmap.csv'),
    'af': load_csv(f'{base}/D_003/agents_frame.csv'),
    'hw': load_csv(f'{base}/D_003/headway.csv'),
    'tp': load_csv(f'{base}/D_003/throughput.csv'),
    'co': load_csv(f'{base}/D_003/collisions.csv'),
    'sum': load_csv(f'{base}/D_003/summary.csv'),
}
sfm = {
    'ec': load_csv(f'{base}/D_003_SFM/evacuation_curve.csv'),
    'fd': load_csv(f'{base}/D_003_SFM/fundamental_diagram.csv'),
    'sf': load_csv(f'{base}/D_003_SFM/specific_flow_series.csv'),
    'hm': load_csv(f'{base}/D_003_SFM/density_heatmap.csv'),
    'af': load_csv(f'{base}/D_003_SFM/agents_frame.csv'),
    'hw': load_csv(f'{base}/D_003_SFM/headway.csv'),
    'tp': load_csv(f'{base}/D_003_SFM/throughput.csv'),
    'co': load_csv(f'{base}/D_003_SFM/collisions.csv'),
    'sum': load_csv(f'{base}/D_003_SFM/summary.csv'),
}

fig = plt.figure(figsize=(20, 24))
gs = gridspec.GridSpec(5, 3, hspace=0.4, wspace=0.35)

# (a) Evacuation Curve
ax = fig.add_subplot(gs[0, 0])
ax.plot(ghost['ec']['time'], ghost['ec']['evacuated_count'], color=C_GHOST, lw=2, label='Ghost (empirical)')
ax.plot(sfm['ec']['time'], sfm['ec']['evacuated_count'], color=C_SFM, lw=2, label='SFM (Helbing 2000)')
total = max(ghost['ec']['evacuated_count'].max(), sfm['ec']['evacuated_count'].max())
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
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    tp = data['tp'].sort_values('time')
    if len(tp) > 1:
        intervals = tp['time'].diff().dropna()
        roll = intervals.rolling(window=10, center=True).mean()
        ax.plot(tp['time'].iloc[1:], roll, color=color, lw=1.2, alpha=0.8, label=f'{label} (10-agent avg)')
ax.set_xlabel('Time [s]')
ax.set_ylabel('Inter-exit interval [s]')
ax.set_title('(b) Time Between Exits')
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


metrics_show = [('RSET 100%', 'rset_100s', 's'), ('RSET 95%', 'rset_95s', 's'), ('RSET 50%', 'rset_50s', 's'),
                ('Throughput', 'throughput_ps', 'p/s'), ('Mean speed', 'mean_speed_ms', 'm/s'),
                ('Peak exit dens.', 'peak_exit_density_pm2', 'p/m²'), ('Min headway', 'min_headway_m', 'm'),
                ('Mean headway', 'mean_headway_m', 'm'), ('Collisions A-A', 'collisions_agent_agent', ''),
                ('Collisions A-W', 'collisions_agent_wall', '')]

table_data = []
for label, key, unit in metrics_show:
    gv = fmt(ghost_dict.get(key, 'N/A'))
    sv = fmt(sfm_dict.get(key, 'N/A'))
    table_data.append([label, f"{gv} {unit}", f"{sv} {unit}"])

table = ax.table(cellText=table_data, colLabels=['Metric', 'Ghost', 'SFM'], cellLoc='center', loc='center',
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
    fd = fd[(fd['density'] > 0) & (fd['density'] < 8) & (fd['speed'] >= 0) & (fd['speed'] < 3)]
    ax.scatter(fd['density'], fd['speed'], c=color, s=2, alpha=0.1)
    bins = np.arange(0.25, min(fd['density'].max() + 0.25, 6), 0.25)
    if len(bins) > 1:
        fc = fd.copy()
        fc['bin'] = pd.cut(fc['density'], bins)
        m = fc.groupby('bin')['speed'].agg(['mean', 'std']).dropna()
        c_ = [(b.left + b.right) / 2 for b in m.index]
        ax.plot(c_, m['mean'].values, color=color, lw=2.5, marker='o', ms=3, label=f'{label} (mean)')
        ax.fill_between(c_, (m['mean'] - m['std']).values, (m['mean'] + m['std']).values, color=color, alpha=0.1)
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Speed [m/s]')
ax.set_title('(d) Fundamental Diagram v(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

# (e) Speed distribution
ax = fig.add_subplot(gs[1, 1])
bins_s = np.arange(0, 2.2, 0.06)
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    sp = data['af']['speed']
    sp = sp[sp < 3]
    ax.hist(sp, bins=bins_s, color=color, alpha=0.45, density=True, label=f'{label} (μ={sp.mean():.2f})')
    ax.axvline(sp.mean(), color=color, ls='--', lw=1.5)
ax.set_xlabel('Speed [m/s]')
ax.set_ylabel('Probability density')
ax.set_title('(e) Speed Distribution')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (f) Headway distribution
ax = fig.add_subplot(gs[1, 2])
bins_h = np.arange(0, 2.5, 0.05)
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    hw = data['hw']['headwayDistance']
    ax.hist(hw, bins=bins_h, color=color, alpha=0.45, density=True, label=f'{label} (μ={hw.mean():.2f}m)')
    ax.axvline(hw.mean(), color=color, ls='--', lw=1.5)
ax.set_xlabel('Headway [m]')
ax.set_ylabel('Probability density')
ax.set_title('(f) Headway Distribution')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)

# (g) Specific flow
ax = fig.add_subplot(gs[2, 0])
for data, color, label in [(ghost, C_GHOST, 'Ghost'), (sfm, C_SFM, 'SFM')]:
    sf = data['sf']
    sf = sf[sf['specific_flow'] < 10]
    w = max(1, len(sf) // 60)
    sm = sf['specific_flow'].rolling(window=w, center=True).mean()
    ax.fill_between(sf['time'], 0, sf['specific_flow'], color=color, alpha=0.06)
    ax.plot(sf['time'], sm, color=color, lw=1.2, label=f'{label}')
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
    af = af[af['speed'] < 3]
    ts = af.groupby(af['time'].round(1))['speed'].mean()
    w = max(1, len(ts) // 40)
    sm = ts.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=color, lw=1.5, label=label)
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
    ac = af.groupby(af['time'].round(1))['agentId'].nunique()
    w = max(1, len(ac) // 40)
    sm = ac.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=color, lw=1.5, label=label)
ax.set_xlabel('Time [s]')
ax.set_ylabel('Active agents')
ax.set_title('(i) Active Agents Over Time')
ax.legend(fontsize=7)
ax.grid(True, alpha=0.3)
ax.set_xlim(0)


# (j,k,l) Heatmaps
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
    im = ax.imshow(diff, cmap='RdBu_r', aspect='equal', interpolation='nearest', vmin=-vabs, vmax=vabs)
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
    bins_t = np.arange(0, co['time'].max() + 5, 5)
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
    th = hw.groupby(hw['time'].round(1))['headwayDistance'].mean()
    w = max(1, len(th) // 25)
    sm = th.rolling(window=w, center=True).mean()
    ax.plot(sm.index, sm.values, color=color, lw=1.5, label=label)
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
    fd = fd[(fd['density'] > 0) & (fd['density'] < 6) & (fd['speed'] < 3) & (fd['speed'] >= 0)]
    flow = fd['density'] * fd['speed']
    ax.scatter(fd['density'], flow, c=color, s=2, alpha=0.1)
    bins = np.arange(0.25, min(fd['density'].max() + 0.25, 6), 0.25)
    if len(bins) > 1:
        fc = fd.copy()
        fc['flow'] = fc['density'] * fc['speed']
        fc['bin'] = pd.cut(fc['density'], bins)
        m = fc.groupby('bin')['flow'].mean().dropna()
        c_ = [(b.left + b.right) / 2 for b in m.index]
        ax.plot(c_, m.values, color=color, lw=2.5, marker='o', ms=3, label=f'{label} (mean)')
ax.set_xlabel('Local density [ped/m²]')
ax.set_ylabel('Flow [ped/m²/s]')
ax.set_title('(o) Flow–Density J(ρ)')
ax.legend(fontsize=8)
ax.grid(True, alpha=0.3)

plt.suptitle('Crossing90 D_003 (entrance 1.2m) - Ghost (Empirical) vs SFM (Helbing 2000)',
             fontsize=14, fontweight='bold', y=1.0)
plt.savefig('./Charts/crossing90_d003.png', dpi=150, bbox_inches='tight')
print("Done")
