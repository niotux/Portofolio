import { el, button, json, notify, newId } from './shared.js';
import { schemaFields, schemaValues } from './settings-form.js';

const RefreshMilliseconds = 2000;
const terminal = status => ['succeeded', 'failed', 'canceled'].includes(status);

// Only the library names these actions. Drafts stay separate from settings and jobs; refreshing progress never
// rebuilds an input form. A request ID survives a lost response and is reused only for the identical request.
export async function renderManagementActions({ id, path, drafts, pane, isCurrent, getStatus }) {
  let state = drafts.get(id);
  if (!state) {
    state = { definition: null, jobs: [], inputs: new Map(), edits: new Map(), selectedAction: null, selectedJob: null, followUp: null, definitionError: null };
    drafts.set(id, state);
  }
  const section = el('section', 'management-actions'); section.setAttribute('aria-label', 'Agent actions');
  const intro = el('div', 'management-intro');
  const execution = el('div', 'management-execution');
  const jobList = el('div', 'management-jobs');
  const details = el('div', 'management-details');
  const editor = el('div', 'management-editor');
  let formKey = null;
  let resultKey = null;
  let actionForm = null;
  let followUpForm = null;

  async function refresh(initial = false) {
    const [definition, jobs] = await Promise.allSettled([read(`${path}/actions`), read(`${path}/jobs`)]);
    if (!isCurrent()) { return; }
    const previousRevision = state.definition?.definitionRevision;
    if (definition.status === 'fulfilled') { state.definition = definition.value; state.definitionError = null; }
    else { state.definition = null; state.definitionError = definition.reason.message; }
    if (jobs.status === 'fulfilled') { state.jobs = jobs.value; }
    else if (initial) { notify(jobs.reason.message, true); }
    if (initial) { pane.replaceChildren(section); section.append(intro, execution, jobList, details, editor); }
    renderIntro();
    if (initial || previousRevision !== state.definition?.definitionRevision) { formKey = null; }
    renderAction(); renderJobs(); renderDetails(); renderEditor(); updateAvailability();
  }

  function renderIntro() {
    const actions = state.definition?.actions || [];
    section.hidden = !actions.length && !state.jobs.length && !state.definitionError && !state.inputs.size;
    intro.replaceChildren(el('h3', '', 'Actions'), el('p', 'muted', 'Run work separately from saving settings. Actions use the saved settings.'));
    if (state.definitionError) { intro.append(el('p', 'error', state.definitionError)); }
    const availability = el('p', 'muted management-availability'); intro.append(availability);
  }

  function actionById(actionId) { return state.definition?.actions.find(action => action.id === actionId); }
  function jobById(jobId) { return state.jobs.find(job => job.id === jobId); }
  function isRunning() { return getStatus() === 'running' && Boolean(state.definition); }

  function updateAvailability() {
    const running = isRunning();
    intro.querySelector('.management-availability').textContent = running ? 'Ready to run.' : 'Start agent to run an action. Settings and previous results remain available.';
    for (const form of [actionForm, followUpForm]) { if (form) { form.submit.disabled = !running || form.busy || form.conflict; } }
  }

  function renderAction() {
    const actions = state.definition?.actions || [];
    if (!actions.some(action => action.id === state.selectedAction) && !state.inputs.has(state.selectedAction)) { state.selectedAction = actions[0]?.id || null; }
    const key = `${state.definition?.definitionRevision}:${state.selectedAction}`;
    if (formKey === key) { return; }
    formKey = key; execution.replaceChildren(); actionForm = null;
    if (!actions.length && !state.inputs.has(state.selectedAction)) { return; }
    const picker = el('select'); picker.setAttribute('aria-label', 'Agent action');
    for (const action of actions) { const option = el('option', '', action.title); option.value = action.id; picker.append(option); }
    if (!actionById(state.selectedAction)) { const option = el('option', '', `${state.inputs.get(state.selectedAction).action.title} (unavailable)`); option.value = state.selectedAction; picker.append(option); }
    picker.value = state.selectedAction;
    picker.addEventListener('change', () => { state.selectedAction = picker.value; renderAction(); updateAvailability(); });
    const label = el('label', 'field'); label.append(el('span', '', 'Action'), picker); execution.append(label);
    const action = actionById(state.selectedAction);
    const draftKey = state.selectedAction;
    if (!state.inputs.has(draftKey)) { state.inputs.set(draftKey, inputDraft({}, action)); }
    actionForm = createForm(action, state.inputs.get(draftKey), {}, () => { formKey = null; renderAction(); updateAvailability(); }); execution.append(actionForm.root);
  }

  function inputDraft(values, action) { return { values: structuredClone(values), changes: new Map(), errors: [], request: null, status: '', revision: state.definition.definitionRevision, action: structuredClone(action) }; }

  function createForm(currentAction, draft, initialValues, rebuild) {
    const action = draft.action;
    const root = el('section', 'management-action-form'); root.setAttribute('aria-label', action.title);
    root.append(el('h4', '', action.title));
    if (action.description) { root.append(el('p', 'muted', action.description)); }
    const fields = el('div'); const status = el('p', 'muted management-form-status', draft.status);
    const form = { root, submit: null, busy: false, conflict: !currentAction || draft.revision !== state.definition?.definitionRevision };
    if (form.conflict) {
      const conflict = el('section', 'settings-conflict'); conflict.setAttribute('aria-label', 'Action definition conflict');
      conflict.append(el('h4', '', 'The action definition changed'), el('p', '', 'Your draft remains below with its previous form. Review the updated form before running it.'));
      if (currentAction) {
        const latest = el('section'); latest.setAttribute('aria-label', 'Updated action form'); latest.append(el('h4', '', 'Updated form'));
        latest.append(schemaFields(currentAction.inputSchema, {}, new Map(), [], () => {}));
        for (const control of latest.querySelectorAll('input,textarea,select')) { control.disabled = true; }
        conflict.append(latest);
        function useLatest(values) {
          draft.values = values; draft.changes.clear(); draft.errors = []; draft.request = null;
          draft.action = structuredClone(currentAction); draft.revision = state.definition.definitionRevision;
          draft.status = 'Review the updated values, then run'; rebuild();
        }
        conflict.append(button('Keep my draft in the updated form', () => useLatest(schemaValues(draft.values, draft.changes))), button('Discard draft and use the updated form', () => useLatest(structuredClone(initialValues))));
      } else { conflict.append(el('p', 'error', 'This action is no longer available. Your draft is kept for reference.')); }
      root.append(conflict);
    }
    function renderFields() { fields.replaceChildren(schemaFields(action.inputSchema, draft.values, draft.changes, draft.errors, () => { draft.status = 'Unsaved action input'; status.textContent = draft.status; })); }
    form.submit = button(action.title, async () => {
      if (form.conflict || !isRunning()) { return; }
      const input = schemaValues(draft.values, draft.changes);
      const body = { actionId: action.id, definitionRevision: draft.revision, input };
      const fingerprint = JSON.stringify(body);
      if (!draft.request || draft.request.fingerprint !== fingerprint) { draft.request = { fingerprint, id: newId() }; }
      body.requestId = draft.request.id;
      form.busy = true; updateAvailability();
      try {
        const response = await fetch(`${path}/jobs`, json('POST', body));
        const result = await response.json().catch(() => ({}));
        if (!response.ok) {
          const fieldErrors = result.fieldErrors || result.errors;
          if (fieldErrors) { draft.errors = fieldErrors; renderFields(); }
          draft.status = result.error || `Action could not be queued (${response.status}).`; status.textContent = draft.status;
          if (response.status === 409) { await refresh(); }
          return;
        }
        draft.errors = []; draft.values = input; draft.changes.clear(); draft.request = null;
        draft.status = 'Queued'; status.textContent = draft.status; renderFields();
        state.selectedJob = result.id; state.followUp = null; resultKey = null;
        await refresh();
      } catch (error) {
        draft.status = `${error.message} Retry to check the same request.`; status.textContent = draft.status;
      } finally { form.busy = false; updateAvailability(); }
    }, 'primary');
    renderFields(); root.append(fields, form.submit, status); return form;
  }

  function renderJobs() {
    jobList.replaceChildren(el('h4', '', 'Recent work'));
    if (!state.jobs.length) { jobList.append(el('p', 'muted', 'No actions have run for this agent.')); return; }
    for (const job of state.jobs) {
      const row = el('div', 'management-job'); row.dataset.jobId = job.id;
      const copy = el('div', 'management-job-copy');
      copy.append(button(job.action?.title || actionById(job.actionId)?.title || 'Unavailable action', () => {
        state.selectedJob = job.id; state.followUp = null; resultKey = null; renderJobs(); renderDetails(); renderEditor();
      }, 'management-job-open'), el('time', 'muted', formatTime(job.createdAt)));
      row.append(copy, el('span', 'management-job-status', job.status));
      row.classList.toggle('selected', state.selectedJob === job.id);
      if (!terminal(job.status)) { row.append(button('Cancel', async () => { await read(`${path}/jobs/${job.id}/cancel`, { method: 'POST' }); await refresh(); }, 'small')); }
      jobList.append(row);
    }
  }

  function renderDetails() {
    const job = jobById(state.selectedJob);
    details.replaceChildren();
    if (!job) { return; }
    details.setAttribute('aria-label', 'Selected work'); details.setAttribute('role', 'region');
    details.append(el('h4', '', job.action?.title || 'Action'), el('p', 'management-job-status', job.status));
    if (job.progress !== null && job.progress !== undefined) {
      const progress = el('progress'); progress.max = 100; progress.value = job.progress; progress.setAttribute('aria-label', 'Work progress');
      details.append(progress, el('span', 'muted', `${job.progress}%`));
    }
    if (job.message) { details.append(el('p', '', job.message)); }
    if (job.error) { details.append(el('p', 'error', job.error)); }
    if (job.status !== 'succeeded') { return; }
    const result = el('section', 'management-result'); result.setAttribute('aria-label', 'Job result');
    result.append(el('h4', '', 'Result'));
    if (job.action?.resultSchema) {
      result.append(schemaFields(job.action.resultSchema, job.result, new Map(), [], () => {}));
      for (const control of result.querySelectorAll('input,textarea,select')) { control.disabled = true; }
    } else { appendValues(result, job.result); }
    details.append(result);
    const followUps = el('div', 'project-settings-actions');
    for (const actionId of job.action?.followUpActions || []) {
      const action = actionById(actionId);
      if (!action) { continue; }
      followUps.append(button(action.title, () => { state.followUp = action.id; resultKey = null; renderEditor(); }));
    }
    details.append(followUps);
  }

  function renderEditor() {
    const job = jobById(state.selectedJob);
    const action = actionById(state.followUp);
    const key = `${job?.id}:${state.followUp}:${state.definition?.definitionRevision}`;
    if (resultKey === key) { return; }
    resultKey = key; editor.replaceChildren(); followUpForm = null;
    const draftKey = `${job?.id}:${state.followUp}`;
    if (!job || job.status !== 'succeeded' || (!action && !state.edits.has(draftKey)) || !(job.action?.followUpActions || []).includes(state.followUp)) { return; }
    if (!state.edits.has(draftKey)) { state.edits.set(draftKey, inputDraft(job.result, action)); }
    editor.append(el('p', 'muted', 'Review and edit this result before running the next action.'));
    followUpForm = createForm(action, state.edits.get(draftKey), job.result, () => { resultKey = null; renderEditor(); }); editor.append(followUpForm.root);
    updateAvailability();
  }

  async function poll() {
    if (!isCurrent()) { return; }
    try { await refresh(); }
    catch (error) { if (isCurrent()) { notify(error.message, true); } }
    if (isCurrent()) { setTimeout(poll, RefreshMilliseconds); }
  }

  await refresh(true);
  if (isCurrent()) { setTimeout(poll, RefreshMilliseconds); }
}

function appendValues(container, values) {
  const list = el('dl', 'settings-facts');
  for (const [name, value] of Object.entries(values || {})) {
    const entry = el('dd');
    if (value !== null && typeof value === 'object') { appendValues(entry, value); }
    else { entry.textContent = String(value ?? 'Not set'); }
    list.append(el('dt', '', name), entry);
  }
  container.append(list);
}

function formatTime(value) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '' : date.toLocaleString(); }

async function read(path, options = {}) {
  const response = await fetch(path, options);
  const value = response.status === 204 ? null : await response.json().catch(() => ({}));
  if (!response.ok) { throw new Error(value?.error || `Could not load agent actions (${response.status}).`); }
  return value;
}
