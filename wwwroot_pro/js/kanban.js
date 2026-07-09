// kanban.js — Entry point: re-exports all kanban sub-modules
// IMPORTANT: every import of a given kanban sub-module MUST use the exact same
// ?v= version across ALL files. A mismatch makes the browser load the module
// twice, duplicating the shared `state` object (empties tiles on re-render and
// makes every hover action button appear because visibleActionsMap is empty).
export { buildKanban, refreshKanban, updateKanbanSummary, startBatDecisionPolling, stopBatDecisionPolling } from './kanban/kanban-core.js?v=41';
export { refreshKanbanColumnOperator } from './kanban/kanban-cards.js?v=41';
export { openPrintDialog, openActionsDropdown, openAssignDropdown, showFaconnageAlerts } from './kanban/kanban-actions.js?v=41';
