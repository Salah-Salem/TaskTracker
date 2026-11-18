import { describe, expect, it } from 'vitest';
import { can } from '../lib/permissions';
import { diffPatch } from '../tasks/TaskFormModal';
import { activeFilterCount, parseTaskQuery } from '../tasks/taskQuery';

describe('permissions (mirror of SETUP_PRO.md §6.2)', () => {
  it.each([
    ['Owner', true, true, true, true],
    ['Member', true, true, false, false],
    ['Viewer', false, false, false, false],
  ] as const)('%s: create=%s edit=%s deleteTask=%s manageMembers=%s', (role, create, edit, del, manage) => {
    expect(can.createTask(role)).toBe(create);
    expect(can.editTask(role)).toBe(edit);
    expect(can.comment(role)).toBe(create);
    expect(can.deleteTask(role)).toBe(del);
    expect(can.manageMembers(role)).toBe(manage);
  });

  it('comment/time-log edit: author or Owner only', () => {
    expect(can.editAuthored('Viewer', 'me', 'me')).toBe(true);
    expect(can.editAuthored('Member', 'someone', 'me')).toBe(false);
    expect(can.editAuthored('Owner', 'someone', 'me')).toBe(true);
  });

  it('Viewers cannot be assignees', () => {
    expect(can.beAssigned('Viewer')).toBe(false);
    expect(can.beAssigned('Member')).toBe(true);
  });

  it('no role (still loading) grants nothing', () => {
    expect(can.createTask(undefined)).toBe(false);
  });
});

describe('diffPatch', () => {
  const base = { title: 'T', description: 'd', status: 'Todo', priority: 'Medium', assigneeId: 'u1', dueDate: '2030-01-01' } as const;

  it('sends only changed fields', () => {
    expect(diffPatch(base, { ...base, status: 'Done' })).toEqual({ status: 'Done' });
  });

  it('turns cleared optional fields into null (unassign, remove due date, clear description)', () => {
    expect(diffPatch(base, { ...base, assigneeId: '', dueDate: '', description: '  ' }))
      .toEqual({ assigneeId: null, dueDate: null, description: null });
  });

  it('sends nothing when nothing changed', () => {
    expect(diffPatch(base, { ...base, title: ' T ' })).toEqual({});
  });
});

describe('parseTaskQuery', () => {
  it('reads repeated filters and drops unknown enum values', () => {
    const q = parseTaskQuery(new URLSearchParams('status=Todo&status=Bogus&priority=High&assignee=none&sort=dueDate:asc&page=3'));
    expect(q).toMatchObject({ status: ['Todo'], priority: ['High'], assigneeId: 'none', sortBy: 'dueDate', sortDir: 'asc', page: 3 });
    expect(activeFilterCount(q)).toBe(3);
  });

  it('defaults to newest first, page 1', () => {
    expect(parseTaskQuery(new URLSearchParams())).toMatchObject({ sortBy: 'createdAt', sortDir: 'desc', page: 1 });
  });
});
