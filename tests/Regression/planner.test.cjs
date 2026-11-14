// Run with: node --test tests\Regression\planner.test.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const html = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'Planner', 'planner.html'), 'utf8');
const engineScript = html.match(/<script>([\s\S]*?)<\/script>/)[1];
const context = { module: { exports: {} } };
vm.runInNewContext(engineScript, context);
const E = context.module.exports;
for (const match of html.matchAll(/<script>([\s\S]*?)<\/script>/g)) new vm.Script(match[1]);
const crystalContext = { fin: Number.isFinite };
vm.runInNewContext(html.slice(html.indexOf('const CRYSTAL_SLOTS ='), html.indexOf('const crystalReady =')) +
  '\nglobalThis.options = crystalOptions;', crystalContext);
const ids = Object.keys(E.WAR_MACHINES);
const role = id => E.WAR_MACHINES[id].spec;
const machine = (id, level = 1, extra = {}) => ({ id, level, owned: true, ...extra });
const account = (machines, heroes = []) => ({ machines, heroes, engineerLevel: 1 });
const score = (a, m, mode, crew = []) => {
  const stats = E.machineStats(m, a, crew);
  return mode === 'arena' ? stats.arenaPower : stats.battlePower;
};
const plain = value => JSON.parse(JSON.stringify(value));

test('balanced crystal rows use server priority, cheaper boundary ties and exact hero targeting', () => {
  const item = (hero, slot, cost) => ({ hero, slot, cost, level: 0, powerGain: 100,
    values: { balanced: { all: (slot === 6 ? 4 : 1) / cost, hero: (slot === 6 ? 4 : 1) / cost } } });
  const ring = item(1, 6, 239), relic = item(2, 7, 60), wrist = item(3, 3, 60);
  assert.equal(crystalContext.options([ring, relic, wrist], 'balanced', -1)[0].slot, 6);
  ring.cost = 240; ring.values.balanced.all = 4 / 240;
  assert.equal(crystalContext.options([ring, relic, wrist], 'balanced', -1)[0].slot, 7);
  ring.cost = 241; ring.values.balanced.all = 4 / 241;
  assert.equal(crystalContext.options([ring, relic, wrist], 'balanced', -1)[2].slot, 6);
  assert.deepEqual(plain(crystalContext.options([ring, relic, wrist], 'balanced', 1)).map(i => i.hero), [1]);
  assert.equal(crystalContext.options([{ ...ring, cost: NaN }], 'balanced', -1).length, 0);
  assert.match(html, /<select id="crystal-goal"[^>]*>\s*<option value="balanced">/);
});

for (const mode of ['campaign', 'arena']) {
  test(`${mode}: low-power healer and tank both survive stronger damage machines`, () => {
    const a = account(ids.map(id => machine(id, role(id) === 'damage' ? 100 : 1)));
    const r = E.optimize(a, { mode, requireTank: true });
    assert.equal(r.squad.length, 5);
    assert.equal(new Set(r.squad).size, 5);
    assert.ok(r.squad.some(id => role(id) === 'tank'));
    assert.ok(r.squad.some(id => role(id) === 'healer'));
    const damage = a.machines.filter(m => role(m.id) === 'damage')
      .sort((x, y) => score(a, y, mode) - score(a, x, mode));
    assert.deepEqual(r.squad.filter(id => role(id) === 'damage').sort(),
      plain(damage.slice(0, 3).map(m => m.id).sort()));
  });

  test(`${mode}: preserve a sole tank when inserting a healer`, () => {
    const a = account([
      ...ids.filter(id => role(id) === 'damage').map(id => machine(id, 100)),
      machine('fortress', 50), machine('hunter'),
    ]);
    const r = E.optimize(a, { mode, requireTank: true });
    assert.ok(r.squad.includes('fortress'));
    assert.ok(r.squad.includes('hunter'));
  });

  test(`${mode}: lineup maximizes solo power subject to owned role requirements`, () => {
    const a = account(ids.map((id, i) => machine(id, i * 7 + 1)));
    const r = E.optimize(a, { mode, requireTank: true });
    let best = -Infinity;
    for (let mask = 0; mask < 1 << ids.length; mask++) {
      const squad = a.machines.filter((_, i) => mask & (1 << i));
      if (squad.length !== 5 || !squad.some(m => role(m.id) === 'tank') ||
          !squad.some(m => role(m.id) === 'healer')) continue;
      best = Math.max(best, squad.reduce((total, m) => total + score(a, m, mode), 0));
    }
    assert.ok(Math.abs(r.squadPower - best) < 1e-8);
  });

  test(`${mode}: unavailable roles, small rosters and empty accounts remain valid`, () => {
    for (const roster of [
      [], ['hunter'], ['hunter', 'fortress'], ['aegis', 'hunter', 'fortress'],
      ids.filter(id => role(id) !== 'healer'),
      ids.filter(id => role(id) !== 'tank'),
      ids.filter(id => role(id) === 'damage'),
    ]) {
      const a = account(roster.map(id => machine(id)));
      const r = E.optimize(a, { mode, requireTank: true });
      assert.equal(r.squad.length, Math.min(5, roster.length));
      assert.equal(new Set(r.squad).size, r.squad.length);
      for (const spec of ['tank', 'healer']) {
        if (roster.some(id => role(id) === spec))
          assert.ok(r.squad.some(id => role(id) === spec));
      }
      assert.ok(Number.isFinite(r.squadPower));
    }
    const a = account(ids.map(id => machine(id, 1, { owned: role(id) === 'damage' })));
    assert.ok(E.optimize(a, { mode, requireTank: true }).squad.every(id => role(id) === 'damage'));
  });

  test(`${mode}: crew is unique, capped and locked assignments survive`, () => {
    const heroes = Array.from({ length: 8 }, (_, i) => ({
      id: `hero-${i}`, spec: ['damage', 'tank', 'healer'][i % 3], jewels: {},
    }));
    const a = { ...account(ids.map(id => machine(id)), heroes), crewSlots: 1 };
    const before = JSON.stringify(a);
    const r = E.optimize(a, { mode, requireTank: true, locked: { 'hero-0': 'fortress' } });
    assert.deepEqual(plain(r.crews.fortress), ['hero-0']);
    const assigned = Object.values(r.crews).flat();
    assert.equal(assigned.length, 5);
    assert.equal(new Set(assigned).size, 5);
    assert.ok(Object.values(r.crews).every(crew => crew.length <= a.crewSlots));
    const expected = r.squad.reduce((total, id) => total + score(a,
      a.machines.find(m => m.id === id), mode,
      r.crews[id].map(hid => heroes.find(h => h.id === hid))), 0);
    assert.equal(r.squadPower, expected);
    assert.equal(JSON.stringify(a), before);
  });
}

test('default options still require a healer, without forcing a tank', () => {
  const a = account(ids.map(id => machine(id, role(id) === 'damage' ? 100 : 1)));
  const r = E.optimize(a);
  assert.ok(r.squad.some(id => role(id) === 'healer'));
  assert.ok(!r.squad.some(id => role(id) === 'tank'));
});

test('Arena machine selection differs from Campaign when power rankings differ', () => {
  const a = account([
    machine('aegis', 1, { basicOverride: { damage: 1e12, health: 5100, armor: 115 } }),
    ...['cloudfist', 'firecracker', 'talos', 'harvester', 'judgement'].map(id => machine(id, 300)),
    machine('hunter'), machine('fortress'),
  ]);
  const campaign = E.optimize(a, { mode: 'campaign', requireTank: true });
  const arena = E.optimize(a, { mode: 'arena', requireTank: true });
  assert.ok(campaign.squad.includes('aegis'));
  assert.ok(!arena.squad.includes('aegis'));
});

test('greedy crew fill uses Arena marginal power, not Campaign power', () => {
  const heroes = [{ id: 'hero', spec: 'healer', jewels: {} }];
  const a = { ...account([
    machine('aegis', 1, { basicOverride: { damage: 1e12, health: 5100, armor: 115 } }),
    machine('hunter', 300),
  ], heroes), crewSlots: 1 };
  const campaign = E.optimize(a, { mode: 'campaign' });
  const arena = E.optimize(a, { mode: 'arena' });
  assert.deepEqual(plain(campaign.crews), { aegis: ['hero'], hunter: [] });
  assert.deepEqual(plain(arena.crews), { aegis: [], hunter: ['hero'] });
});
