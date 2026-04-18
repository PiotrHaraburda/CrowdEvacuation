import json
import numpy as np

print("NarrowDoor LC_001")

json_path = 'data_processed/narrow_door/LC_001.json'
data_offset_x = -0.3347
data_offset_y = 0

with open(json_path, 'r') as f:
    agents = json.load(f)

start_x = []
start_z = []

for agent in agents:
    if len(agent['x']) == 0:
        continue
    x0 = agent['x'][0] - data_offset_x
    z0 = agent['y'][0] - data_offset_y
    start_x.append(x0)
    start_z.append(z0)

start_x = np.array(start_x)
start_z = np.array(start_z)

print(f"File: {json_path}")
print(f"  Agent count: {len(agents)}")
print(f"  Spawn rate: 0")
print(f"  Start X range: [{start_x.min():.3f}, {start_x.max():.3f}]")
print(f"  Start Z range: [{start_z.min():.3f}, {start_z.max():.3f}]")
print()




print("Crossing90 D_003 - all agents")
with open('data_processed/crossing/D_003.json', 'r') as f:
    all_agents = json.load(f)

ns_agents = []
ew_agents = []
for agent in all_agents:
    if len(agent['x']) == 0:
        continue
    z0 = agent['y'][0]
    x0 = agent['x'][0]
    if z0 > 4:
        ns_agents.append(agent)
    elif x0 > 4:
        ew_agents.append(agent)

print(f"Total agents: {len(all_agents)}")
print(f"N->S stream: {len(ns_agents)} agents")
print(f"E->W stream: {len(ew_agents)} agents")
print()

print("Crossing90 D_003 - N to S stream")
for a in ns_agents:
    a['_t0'] = a['t'][0]
ns_sorted = sorted(ns_agents, key=lambda a: a['_t0'])
ns_times = [a['t'][0] for a in ns_agents]
ns_x0 = [a['x'][0] for a in ns_agents]
ns_z0 = [a['y'][0] for a in ns_agents]
duration = max(ns_times) - min(ns_times)
print(f"  Agent count: {len(ns_agents)}")
print(f"  First start: {min(ns_times):.2f}s, Last start: {max(ns_times):.2f}s")
print(f"  Spawn duration: {duration:.2f}s, Rate: {len(ns_agents)/duration:.2f} agents/s")
print(f"  Start X range: [{min(ns_x0):.3f}, {max(ns_x0):.3f}]")
print(f"  Start Z range: [{min(ns_z0):.3f}, {max(ns_z0):.3f}]")
print()

print("Crossing90 D_003 - E to W stream")
ew_times = [a['t'][0] for a in ew_agents]
ew_x0 = [a['x'][0] for a in ew_agents]
ew_z0 = [a['y'][0] for a in ew_agents]
duration = max(ew_times) - min(ew_times)
print(f"  Agent count: {len(ew_agents)}")
print(f"  First start: {min(ew_times):.2f}s, Last start: {max(ew_times):.2f}s")
print(f"  Spawn duration: {duration:.2f}s, Rate: {len(ew_agents)/duration:.2f} agents/s")
print(f"  Start X range: [{min(ew_x0):.3f}, {max(ew_x0):.3f}]")
print(f"  Start Z range: [{min(ew_z0):.3f}, {max(ew_z0):.3f}]")


