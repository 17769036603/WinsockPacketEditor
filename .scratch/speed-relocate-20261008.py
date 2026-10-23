import sys, json, pathlib, struct
sys.path.insert(0, str(pathlib.Path('tools/enemy-status-panel').resolve()))
import battle_reader as b
adb = r'C:\Users\Administrator\AppData\Local\Android\Sdk\platform-tools\adb.exe'
serial = 'emulator-5554'
package = 'com.gdoo.dhxy'
pid = b._pid(adb, serial, package)
identity = b._identity(adb, serial, pid)
cache = b._root_adb(adb, serial, 'cat /data/user/0/com.xnas.wpe.battlepanel/cache/manager-cache-v2-dhxy').decode()
kv = dict(line.split('=', 1) for line in cache.splitlines() if '=' in line)
assert int(kv['pid']) == pid and int(kv['start_ticks']) == identity['startTicks']
manager = int(kv['manager'], 16)
ranges = b.lua.parse_maps(b._root_adb(adb, serial, f'cat /proc/{pid}/maps'))
r = b.lua.BoundedPageMemoryReader(adb, serial, pid, ranges, 8)
try:
    mf, battle, round_no, roles, positions = b._manager_values(r, manager)
    player = b._unwrap(r, mf['m_PlayerId'])
    pm = b._position_mapping(r, positions)
    ui = b._userdata_index(r, roles)
    output = {'identity': identity, 'battle': battle, 'round': round_no, 'managerFields': sorted(mf), 'roles': [], 'functions':{}}
    def function_dump(value):
        addr = b.lua.tagged_pointer_from_value(value, 0xfffffff7)
        assert addr
        fh = r.read(addr, 40)
        assert fh[9] == 8 and fh[10] == 0, 'not Lua function'
        pc = struct.unpack_from('<Q', fh, 32)[0]
        proto = pc - 104
        ph = r.read(proto, 104)
        assert ph[9] == 7
        nbc = struct.unpack_from('<I', ph, 12)[0]
        k = struct.unpack_from('<Q', ph, 32)[0]
        nkgc = struct.unpack_from('<I', ph, 48)[0]
        assert 0 < nbc < 8192 and nkgc < 1024
        constants = []
        for i in range(nkgc):
            ptr = struct.unpack('<Q', r.read(k-(i+1)*8, 8))[0] & ((1<<47)-1)
            head = r.read(ptr, 16)
            constants.append(b.lua.read_gcstr(r,ptr) if head[9] == 4 else {'type':head[9]})
        return {'proto':hex(proto),'parameters':ph[10],'constants':constants,'bytecode':r.read(pc,nbc*4).hex()}
    manager_methods = dict(mf)
    cls = b.lua.tagged_pointer_from_value(mf.get('class', {}), b.lua.LJ_TTAB)
    if cls: manager_methods.update(b._field_map(r, cls))
    output['managerMethods'] = sorted(manager_methods)
    for name in ('setBattleRole','initBattlefieldData','convertRoleData','setRoundCmdList'):
        if name in manager_methods:
            try: output['functions']['manager.'+name] = function_dump(manager_methods[name])
            except (AssertionError,b.ReadFailure): pass
    for role_id, pos in pm.items():
        if pos not in ui:
            continue
        env = b._userdata_env(r, ui[pos])
        fields = b._field_map(r, env)
        vals = {k: b._unwrap(r, fields.get(k)) for k in ('m_RoleId','m_Pos','m_IsAlive','m_IsLocalPlayer')}
        battlefield = b.lua.tagged_pointer_from_value(fields.get('m_Battlefield', {}), b.lua.LJ_TTAB)
        if vals['m_RoleId'] != role_id or vals['m_Pos'] != pos or battlefield != manager:
            continue
        item = {'pos':pos, 'local':role_id == player, 'alive':vals['m_IsAlive'], 'fields':fields, 'nested':{}}
        for name, value in fields.items():
            ptr = b.lua.tagged_pointer_from_value(value, b.lua.LJ_TTAB)
            if ptr and any(token in name.lower() for token in ('data','prop','attr','info','speed','class')):
                try: item['nested'][name] = b._field_map(r, ptr)
                except b.ReadFailure: pass
        output['roles'].append(item)
        if 'role.initData' not in output['functions']:
            output['functions']['role.initData'] = function_dump(fields['initData'])
            output['functions']['role.getShowData'] = function_dump(fields['getShowData'])
        if len(output['roles']) >= 3: break
    r.pages.clear(); r.accessed_pages.clear()
    _, after_battle, after_round, after_roles, after_positions = b._manager_values(r, manager)
    assert b._identity(adb, serial, pid) == identity
    assert (after_battle, after_round, after_roles, after_positions) == (battle, round_no, roles, positions), 'battle changed during probe'
    path = pathlib.Path('.scratch/speed-relocate-current.json')
    path.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'battle':battle,'round':round_no,'roles':len(output['roles']),'functions':{k:v['constants'] for k,v in output['functions'].items()}}, ensure_ascii=False))
finally:
    r.close()
