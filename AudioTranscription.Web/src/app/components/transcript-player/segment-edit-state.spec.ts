import { SegmentEditState } from './segment-edit-state';

describe('SegmentEditState', () => {
  let state: SegmentEditState;

  beforeEach(() => {
    state = new SegmentEditState();
  });

  it('is idle at first and cannot save', () => {
    expect(state.index()).toBeNull();
    expect(state.canSave()).toBe(false);
  });

  it('starts editing with the current text as draft', () => {
    state.start(2, 'Hallo Welt');

    expect(state.index()).toBe(2);
    expect(state.draft()).toBe('Hallo Welt');
    expect(state.error()).toBeNull();
  });

  it('can only save a changed, non-empty draft', () => {
    state.start(0, 'Hallo');
    expect(state.canSave()).toBe(false);

    state.draft.set('  Hallo  ');
    expect(state.canSave()).toBe(false);

    state.draft.set('   ');
    expect(state.canSave()).toBe(false);

    state.draft.set('Hallo Welt');
    expect(state.canSave()).toBe(true);
  });

  it('cannot save while a save is running', () => {
    state.start(0, 'Hallo');
    state.draft.set('Hallo Welt');

    state.beginSave();

    expect(state.saving()).toBe(true);
    expect(state.canSave()).toBe(false);
  });

  it('keeps the draft and shows the error when saving fails', () => {
    state.start(0, 'Hallo');
    state.draft.set('Hallo Welt');
    state.beginSave();

    state.fail('conflict');

    expect(state.saving()).toBe(false);
    expect(state.error()).toBe('conflict');
    expect(state.index()).toBe(0);
    expect(state.draft()).toBe('Hallo Welt');
    expect(state.canSave()).toBe(true);
  });

  it('returns to idle after a successful save or a cancel', () => {
    state.start(0, 'Hallo');
    state.beginSave();
    state.finish();
    expect(state.index()).toBeNull();
    expect(state.saving()).toBe(false);

    state.start(1, 'Los');
    state.cancel();
    expect(state.index()).toBeNull();
    expect(state.draft()).toBe('');
  });

  it('forgets an earlier error when editing another segment', () => {
    state.start(0, 'Hallo');
    state.fail('failed');

    state.start(1, 'Los');

    expect(state.error()).toBeNull();
  });
});
