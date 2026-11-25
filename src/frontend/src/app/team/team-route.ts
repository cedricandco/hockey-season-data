import type { RouteSelection } from './team.models';

/**
 * Association → division → league → team, as the team page holds it. A `null`
 * list is not loaded yet; an empty one is loaded and holds nothing (the caller
 * follows no team below that level).
 */
export interface SelectionState {
  associations: readonly number[] | null;
  divisions: readonly number[] | null;
  leagues: readonly number[] | null;
  teams: readonly number[] | null;
  selection: RouteSelection;
}

/** Fetch the page still needs before the wanted selection is entirely applied. */
export type SelectionAction =
  /** The selection is the wanted one, or was truncated to the deepest valid level. */
  | 'settled'
  /** Read the associations, the entry point of every restore. */
  | 'load-associations'
  | 'load-divisions'
  | 'load-leagues'
  | 'load-teams'
  /** Read the detail of the selected team. */
  | 'load-team';

export interface SelectionStep {
  /** Selection to adopt now; the action, when any, is fetched for it. */
  selection: RouteSelection;
  action: SelectionAction;
}

const EMPTY: RouteSelection = { association: 0, division: 0, league: 0, team: 0 };

export function sameSelection(left: RouteSelection, right: RouteSelection): boolean {
  return left.association === right.association && left.division === right.division
    && left.league === right.league && left.team === right.team;
}

/**
 * Next step towards `wanted`, one level at a time.
 *
 * The page adopts the returned selection, runs the returned action, then asks
 * again: a level that was not read yet answers `load-…`, so the restore
 * survives the asynchronous reads without racing a second navigation.
 *
 * A level absent from the URL — or that does not exist in the follows — stops
 * the restore at the deepest valid selection: the deeper levels are cleared,
 * which is what makes the page unable to offer a path outside the follows.
 * `wanted` is never adopted blindly.
 */
export function nextSelectionStep(state: SelectionState, wanted: RouteSelection): SelectionStep {
  if (sameSelection(state.selection, wanted)) return { selection: state.selection, action: 'settled' };
  if (state.associations === null) return { selection: state.selection, action: 'load-associations' };

  const { association } = wanted;
  const current = state.selection;
  if (!association || !state.associations.includes(association)) {
    return { selection: EMPTY, action: 'settled' };
  }
  if (association !== current.association) {
    return { selection: { ...EMPTY, association }, action: 'load-divisions' };
  }
  if (state.divisions === null) return { selection: current, action: 'load-divisions' };

  if (!wanted.division || !state.divisions.includes(wanted.division)) {
    return { selection: { ...EMPTY, association }, action: 'settled' };
  }
  if (wanted.division !== current.division) {
    return { selection: { ...EMPTY, association, division: wanted.division }, action: 'load-leagues' };
  }
  if (state.leagues === null) return { selection: current, action: 'load-leagues' };

  if (!wanted.league || !state.leagues.includes(wanted.league)) {
    return { selection: { ...EMPTY, association, division: wanted.division }, action: 'settled' };
  }
  if (wanted.league !== current.league) {
    return {
      selection: { ...EMPTY, association, division: wanted.division, league: wanted.league },
      action: 'load-teams',
    };
  }
  if (state.teams === null) return { selection: current, action: 'load-teams' };

  if (!wanted.team || !state.teams.includes(wanted.team)) {
    return {
      selection: { association, division: wanted.division, league: wanted.league, team: 0 },
      action: 'settled',
    };
  }
  return { selection: { ...current, team: wanted.team }, action: 'load-team' };
}
