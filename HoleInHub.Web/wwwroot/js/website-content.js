const root = document.querySelector('.website-content');
const find = (selector, within = root) => within.querySelector(selector);
const setText = (within, selector, text) => { find(selector, within).textContent = text; };
const editor = find('[data-editor]');
const editorForm = find('form', editor);
const offerFields = editor.querySelectorAll('[data-offer-fields]');
const listHeading = find('[data-list-heading]');
const listControls = find('[data-content-controls]');
const editorHeading = find('#content-editor-heading', editor);
const upload = editorForm.elements.upload;
const imagePreview = find('[data-upload-preview]');
const uploadArea = find('[data-upload-area]');
const uploadEmpty = find('[data-upload-empty]');
const uploadSelected = find('[data-upload-selected]');
const categoryDialog = find('[data-categories]');
const categoryForm = find('form', categoryDialog);
const deleteDialog = find('[data-delete]');
const confirmDelete = find('[data-confirm-delete]', deleteDialog);
const sectionDrawer = find('[data-section-drawer]');
const sectionForm = find('[data-section-settings]');
const panels = Object.fromEntries([...root.querySelectorAll('[data-content-list]')]
    .map(panel => [panel.dataset.contentList, panel]));
const cancelReorder = {};
const publishState = checkbox => checkbox.checked ? 'Published' : 'Hidden';
const readSection = () => ({ ...read(sectionForm), visibility: publishState(sectionForm.elements.visibility) });
const read = form => Object.fromEntries([...new FormData(form)].map(([key, value]) =>
    [key, typeof value === 'string' ? value.trim() : value]));
let state;
let galleryCategories;
let offerCategories;
let lists;
let sectionSettings;
let saving = false;

function applyState(next) {
    state = next;
    galleryCategories = next.galleryCategories;
    lists = { gallery: next.gallery, offers: next.offers };
    sectionSettings = next.section;
}

async function request(options) {
    const response = await fetch(root.dataset.contentUrl, { ...options, headers: {
        Accept: 'application/json',
        RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value
    } });
    if (response.redirected || response.status === 401 || response.status === 403)
        throw new Error('Sign in as an admin again, then reload this page.');
    const json = await response.json().catch(() => null);
    if (!response.ok || !json) throw new Error(json?.detail || 'The server could not complete this request.');
    return json;
}

async function persist(next, { file, imageTarget, errorTitle, dialog }) {
    if (saving) return false;
    saving = true;
    try {
        const body = new FormData();
        body.set('state', JSON.stringify(next));
        if (file) {
            body.set('image', file);
            body.set('imageTarget', imageTarget);
        }
        applyState(await request({ method: 'PUT', body }));
        return true;
    } catch (error) {
        const message = error instanceof TypeError ? 'Could not reach the server. Try again.' : error.message;
        window.HoleInHubToasts?.show({ kind: 'error', title: errorTitle, message, dialog });
        return false;
    } finally {
        saving = false;
    }
}

for (const type of ['click', 'submit', 'cancel']) {
    root.addEventListener(type, event => {
        if (!saving) return;
        event.preventDefault();
        event.stopImmediatePropagation();
    }, true);
}
let edit;
let deletion;
let renaming;

function notifySuccess(title, message, dialog) {
    window.HoleInHubToasts?.show({ kind: 'success', title, message, dialog });
}

function error(field, message = '') {
    const container = field.closest('[data-editor-field], label');
    const id = (field.closest('[aria-labelledby]')?.getAttribute('aria-labelledby') ?? 'content-section-settings') + '-error-' + field.name;
    container.querySelector('.content-error')?.remove();
    field.removeAttribute('aria-invalid');
    const controls = field === upload ? [field, ...editor.querySelectorAll('[data-upload-pick]')] : [field];
    for (const control of controls) {
        const descriptions = (control.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(value => value && value !== id);
        if (message) descriptions.push(id);
        if (descriptions.length) control.setAttribute('aria-describedby', descriptions.join(' '));
        else control.removeAttribute('aria-describedby');
    }
    if (!message) return;
    const note = document.createElement('span');
    note.className = 'content-error';
    note.id = id;
    note.textContent = message;
    container.append(note);
    field.setAttribute('aria-invalid', 'true');
}

function validate(form, custom = {}) {
    let firstInvalid;
    for (const field of form.elements) {
        if (!field.name || field.matches(':disabled')) continue;
        const message = custom[field.name] || (field.validity.badInput ? 'Enter a valid number.' :
            field.required && !field.value.trim() ? 'This field is required.' :
            !field.validity.valid ? field.validationMessage : '');
        error(field, message);
        if (message) firstInvalid ??= field;
    }
    if (firstInvalid === upload) find('[data-upload-pick]', imagePreview.hidden ? uploadEmpty : uploadSelected).focus();
    else firstInvalid?.focus();
    return !firstInvalid;
}

function categoriesFor(key) {
    return key === 'gallery' ? galleryCategories : offerCategories;
}

function updateCategoryFilter(key) {
    const filter = find('[data-content-category]', panels[key]);
    const previous = filter.value;
    const names = categoriesFor(key);
    filter.replaceChildren(new Option('All categories', ''), ...names.map(name => new Option(name)));
    if (names.includes(previous)) filter.value = previous;
}

function render(key, focusItem, focusAction) {
    const panel = panels[key];
    const body = find('tbody', panel);
    const rows = lists[key];
    const query = find('[data-content-search]', panel).value.trim().toLowerCase();
    const category = find('[data-content-category]', panel).value;
    const filtered = Boolean(query || category);
    find('[data-filter-note]', panel).hidden = !filtered;
    cancelReorder[key]?.();
    body.replaceChildren();
    rows.forEach((item, index) => {
        if (category && item.category !== category || query && !item.title.toLowerCase().includes(query)) return;
        const row = find('[data-content-row]').content.firstElementChild.cloneNode(true);
        row.dataset.index = index;
        const handle = find('[data-drag-handle]', row);
        handle.disabled = filtered || rows.length < 2;
        handle.setAttribute('aria-label', 'Reorder ' + item.title);
        setText(handle, '[data-service-position]', String(index + 1).padStart(2, '0'));
        const thumbnail = find('img', row);
        thumbnail.hidden = !item.image;
        if (item.image) thumbnail.src = item.image;
        setText(row, '[data-item-title]', item.title);
        setText(row, '[data-item-category]', item.category);
        const visibility = find('[data-action="visibility"]', row);
        visibility.setAttribute('aria-checked', String(item.visibility === 'Published'));
        visibility.setAttribute('aria-label', 'Publish ' + item.title);
        setText(visibility, '[data-visibility-text]', item.visibility);
        for (const button of row.querySelectorAll('.content-icon')) {
            button.setAttribute('aria-label', button.title + ' ' + item.title);
        }
        body.append(row);
        if (item === focusItem) {
            const target = find('[data-action="' + focusAction + '"]', row);
            (target?.disabled ? find('[data-action="edit"]', row) : target)?.focus();
        }
    });

    if (!body.rows.length) {
        const cell = body.insertRow().insertCell();
        cell.colSpan = 5;
        cell.textContent = filtered ? 'No items match your search and category.' :
            key === 'gallery' ? 'No gallery items yet.' : 'No offers yet.';
    }
    if (focusItem && !rows.some((item, index) => item === focusItem && find('tr[data-index="' + index + '"]', body))) {
        find('[data-content-search]', panel).focus();
    }
}

function createRowGhost(row) {
    const ghost = document.createElement('div');
    ghost.className = 'content-row-floating';
    const table = ghost.appendChild(document.createElement('table'));
    table.className = 'content-table';
    const columns = table.appendChild(document.createElement('colgroup'));
    const width = row.getBoundingClientRect().width;
    for (const cell of row.cells) {
        const column = columns.appendChild(document.createElement('col'));
        column.style.width = cell.getBoundingClientRect().width / width * 100 + '%';
    }
    table.createTBody().append(row.cloneNode(true));
    return ghost;
}

function mountContentReorder(key, panel) {
    const body = find('tbody', panel);
    cancelReorder[key] = window.HoleInHubRowReorder(body, {
        rowSelector: 'tr[data-index]',
        createGhost: createRowGhost,
        canReorder: () => !saving && lists[key].length > 1 && find('[data-filter-note]', panel).hidden,
        async commitOrder(row, before) {
            const ordered = [...body.rows];
            const position = ordered.indexOf(row);
            if (before.every((previous, index) => previous === ordered[index])) return;
            const rows = ordered.map(entry => lists[key][Number(entry.dataset.index)]);
            const saved = await persist({ ...state, [key]: rows }, { errorTitle: 'Could not save display order' });
            if (row.parentElement === body) {
                if (saved) {
                    ordered.forEach((entry, index) => { entry.dataset.index = index; });
                } else {
                    before.forEach((entry, index) => {
                        body.append(entry);
                        setText(entry, '[data-service-position]', String(index + 1).padStart(2, '0'));
                    });
                }
                find('[data-drag-handle]', row).focus({ preventScroll: true });
            } else if (saved) {
                render(key);
            }
            if (saved) notifySuccess('Display order saved', rows[position].title + ' moved to position ' + (position + 1) + '.');
        }
    });
}

function toggleButtonFields() {
    const fields = editorForm.elements;
    const visible = edit.key === 'offers' && fields.buttonVisibility.checked;
    for (const field of [fields.buttonText, fields.buttonDestination]) {
        field.disabled = !visible;
        field.closest('[data-editor-field]').hidden = !visible;
        if (!visible) error(field);
    }
}

function openDrawer(drawer) {
    drawer.showModal();
    find('.content-drawer-body', drawer).scrollTop = 0;
    find('[data-close]', drawer).focus();
    if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        drawer.animate([{ transform: 'translateX(100%)' }, { transform: 'translateX(0)' }],
            { duration: 200, easing: 'ease-out' });
    }
}

function openEditor(key, index, trigger) {
    const item = lists[key][index];
    const offers = key === 'offers';
    const title = offers ? 'Offer' : 'Gallery Item';
    edit = { key, item, trigger, image: item?.image ?? '', pendingImage: null, file: null, removed: false, uploadError: '' };
    editorForm.reset();
    for (const field of editorForm.querySelectorAll('[aria-invalid]')) error(field);
    for (const group of offerFields) {
        group.hidden = !offers;
        group.disabled = !offers;
    }
    editorHeading.textContent = (item ? 'Edit ' : 'Add ') + title;
    setText(editor, '[data-details-heading]', offers ? 'Offer details' : 'Gallery item details');
    setText(editor, '[data-title-label]', offers ? 'Card title' : 'Title');
    setText(editor, '[data-description-label]', offers ? 'Card description' : 'Description');
    setText(editor, '[data-category-label]', offers ? 'Category label' : 'Category');
    const fields = editorForm.elements;
    const categories = categoriesFor(key);
    fields.category.replaceChildren(new Option('Select a category', ''), ...categories.map(name => new Option(name)));
    for (const [name, value] of Object.entries(item ?? {})) {
        if (fields.namedItem(name)) fields.namedItem(name).value = value;
    }
    fields.visibility.checked = !item || item.visibility === 'Published';
    fields.buttonVisibility.checked = !item || item.buttonVisibility === 'visible';
    fields.order.value = item ? index + 1 : lists[key].length + 1;
    fields.order.max = lists[key].length + (item ? 0 : 1);
    toggleButtonFields();
    showUpload(edit.image);
    listHeading.hidden = true;
    listControls.hidden = true;
    editor.hidden = false;
    editorHeading.focus({ preventScroll: true });
    window.scrollTo({ top: 0, behavior: 'instant' });
}

function showUpload(url) {
    imagePreview.hidden = !url;
    uploadEmpty.hidden = Boolean(url);
    uploadSelected.hidden = !url;
    if (url) imagePreview.src = url;
    else imagePreview.removeAttribute('src');
}

function releaseImage(url) {
    if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
}

async function selectImage(file) {
    const draft = edit;
    const pickerHadFocus = uploadSelected.contains(document.activeElement);
    releaseImage(draft.pendingImage);
    draft.pendingImage = null;
    draft.file = null;
    draft.uploadError = '';
    showUpload(draft.removed ? '' : draft.image);
    if (pickerHadFocus && uploadSelected.hidden) find('[data-upload-pick]', uploadEmpty).focus();
    if (!file) {
        error(upload);
        return;
    }
    if (!['image/png', 'image/jpeg', 'image/webp', 'image/avif', 'image/gif'].includes(file.type) ||
        file.size > 10 * 1024 * 1024) {
        draft.uploadError = 'Choose a PNG, JPEG, WebP, AVIF, or GIF image up to 10 MB.';
        error(upload, draft.uploadError);
        return;
    }
    const url = URL.createObjectURL(file);
    draft.pendingImage = url;
    draft.uploadError = 'Wait for the image preview to load.';
    const image = new Image();
    image.src = url;
    try {
        await image.decode();
        if (edit !== draft || draft.pendingImage !== url || editor.hidden) return;
        draft.uploadError = '';
        draft.file = file;
        const emptyPickerHadFocus = uploadEmpty.contains(document.activeElement);
        showUpload(url);
        if (emptyPickerHadFocus) find('[data-upload-pick]', uploadSelected).focus();
    } catch {
        if (edit !== draft || draft.pendingImage !== url || editor.hidden) return;
        releaseImage(url);
        draft.pendingImage = null;
        draft.uploadError = 'This file could not be opened as an image. Choose another image.';
    }
    error(upload, draft.uploadError);
}

upload.addEventListener('change', () => { if (upload.files.length) void selectImage(upload.files[0]); });
for (const button of editor.querySelectorAll('[data-upload-pick]')) {
    button.addEventListener('click', () => { upload.value = ''; upload.click(); });
}
find('[data-upload-remove]').addEventListener('click', () => {
    releaseImage(edit.pendingImage);
    edit.pendingImage = null;
    edit.file = null;
    edit.removed = true;
    edit.uploadError = '';
    upload.value = '';
    error(upload);
    showUpload('');
    find('[data-upload-pick]', uploadEmpty).focus();
});
uploadArea.addEventListener('dragover', event => {
    if (!event.dataTransfer.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    uploadArea.dataset.dragOver = '';
});
uploadArea.addEventListener('dragleave', event => {
    if (!uploadArea.contains(event.relatedTarget)) delete uploadArea.dataset.dragOver;
});
uploadArea.addEventListener('drop', event => {
    if (!event.dataTransfer.types.includes('Files')) return;
    event.preventDefault();
    delete uploadArea.dataset.dragOver;
    if (event.dataTransfer.files.length !== 1) {
        edit.uploadError = 'Drop one image at a time.';
        error(upload, edit.uploadError);
        return;
    }
    void selectImage(event.dataTransfer.files[0]);
});

function closeEditor() {
    releaseImage(edit.pendingImage);
    edit = null;
    delete uploadArea.dataset.dragOver;
    showUpload('');
    editor.hidden = true;
    listHeading.hidden = false;
    listControls.hidden = false;
}

for (const control of editor.querySelectorAll('[data-editor-back]')) {
    control.addEventListener('click', event => {
        event.preventDefault();
        const { trigger } = edit;
        closeEditor();
        trigger.focus();
    });
}
editorForm.elements.buttonVisibility.addEventListener('change', toggleButtonFields);

function validDestination(value) {
    if (value.startsWith('/') && !/^[/][\\/]/.test(value) && !value.includes('\\')) return true;
    try { return ['http:', 'https:'].includes(new URL(value).protocol); }
    catch { return false; }
}

editorForm.addEventListener('submit', async event => {
    event.preventDefault();
    const { order, ...values } = read(editorForm);
    delete values.upload;
    values.visibility = publishState(editorForm.elements.visibility);
    if (edit.key === 'offers') values.buttonVisibility = editorForm.elements.buttonVisibility.checked ? 'visible' : 'hidden';
    const validation = { upload: edit.uploadError };
    if (values.buttonVisibility === 'visible') {
        if (!values.buttonText) validation.buttonText = 'Enter the button text.';
        if (!validDestination(values.buttonDestination)) validation.buttonDestination = 'Enter a site path or an HTTP(S) URL.';
    }
    if (!validate(editorForm, validation)) return;
    const { key, item, image, file, removed } = edit;
    const rows = [...lists[key]];
    if (item) rows.splice(rows.indexOf(item), 1);
    const updated = { ...item, ...values, id: item?.id ?? crypto.randomUUID(), image: removed ? '' : image };
    const index = Number(order) - 1;
    rows.splice(index, 0, updated);
    const itemType = key === 'gallery' ? 'Gallery item' : 'Offer';
    const errorTitle = itemType + (item ? ' could not be updated' : ' could not be added');
    if (!await persist({ ...state, [key]: rows }, { file, imageTarget: updated.id, errorTitle })) return;
    closeEditor();
    render(key, lists[key][index], 'edit');
    notifySuccess(itemType + (item ? ' updated' : ' added'), updated.title);
});

function renderCategories() {
    const list = find('[data-category-list]');
    list.replaceChildren();
    for (const name of galleryCategories) {
        const row = document.createElement('li');
        const title = document.createElement('span');
        title.textContent = name;
        row.append(title);
        for (const action of ['Rename', 'Remove']) {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = action;
            button.dataset.categoryAction = action;
            button.dataset.name = name;
            button.setAttribute('aria-label', action + ' ' + name);
            if (action === 'Remove') button.disabled = galleryCategories.length === 1 ||
                lists.gallery.some(item => item.category === name);
            row.append(button);
        }
        list.append(row);
    }
}

function categoryMode(name = '') {
    renaming = name;
    categoryForm.elements.name.value = name;
    error(categoryForm.elements.name);
    const submit = find('[data-category-submit]', categoryForm);
    const label = name ? 'Apply rename' : 'Add category';
    if (window.HoleInHubButtons) window.HoleInHubButtons.setLabel(submit, label);
    else submit.textContent = label;
    find('[data-category-cancel]').hidden = !name;
}

categoryDialog.addEventListener('click', event => {
    const button = event.target.closest('[data-category-action]');
    if (!button) return;
    const { categoryAction, name } = button.dataset;
    if (categoryAction === 'Rename') {
        categoryMode(name);
        categoryForm.elements.name.focus();
        return;
    }
    deletion = { category: name };
    openDeleteConfirmation('Delete category?', 'Delete the category “' + name + '”?', 'Delete category');
});

async function deleteCategory(name) {
    const categories = galleryCategories.filter(category => category !== name);
    if (!await persist({ ...state, galleryCategories: categories }, { errorTitle: 'Could not delete category', dialog: deleteDialog })) return;
    deleteDialog.close();
    categoryMode();
    updateCategoryFilter('gallery');
    render('gallery');
    renderCategories();
    categoryForm.elements.name.focus();
    notifySuccess('Category deleted', name, categoryDialog);
}
find('[data-category-cancel]').addEventListener('click', () => {
    categoryMode();
    categoryForm.elements.name.focus();
});
categoryForm.addEventListener('submit', async event => {
    event.preventDefault();
    const { name } = read(categoryForm);
    const duplicate = galleryCategories.some(category => category !== renaming &&
        category.toLowerCase() === name.toLowerCase());
    if (!validate(categoryForm, { name: duplicate ? 'A category with this name already exists.' : '' })) return;
    const categories = renaming ? galleryCategories.map(category => category === renaming ? name : category) :
        [...galleryCategories, name];
    const gallery = lists.gallery.map(item => item.category === renaming ? { ...item, category: name } : item);
    const errorTitle = renaming ? 'Could not rename category' : 'Could not add category';
    if (!await persist({ ...state, galleryCategories: categories, gallery }, { errorTitle, dialog: categoryDialog })) return;
    notifySuccess(renaming ? 'Category renamed' : 'Category added', name, categoryDialog);
    categoryMode();
    updateCategoryFilter('gallery');
    render('gallery');
    renderCategories();
    categoryForm.elements.name.focus();
});

const offerPreview = find('[data-offer-preview]');
const galleryPreview = find('[data-gallery-preview]');
function fillPreview(container, item) {
    const image = find('img', container);
    image.hidden = !item.image;
    if (item.image) image.src = item.image;
    else image.removeAttribute('src');
    setText(container, '[data-preview-category]', item.category);
    setText(container, '[data-preview-title]', item.title);
    setText(container, '[data-preview-description]', item.description);
}

function showDetails(item) {
    fillPreview(galleryPreview, item);
    find('.gallery-story-scroll', galleryPreview).scrollTop = 0;
    galleryPreview.showModal();
}

function showOffers(item, settings = sectionSettings) {
    const cards = find('[data-preview-cards]');
    cards.replaceChildren();
    const previewHeader = find('[data-preview-section]');
    const visible = settings.visibility === 'Published';
    previewHeader.hidden = Boolean(item) || !visible;
    const items = item ? [item] : visible ? lists.offers.filter(offer => offer.visibility === 'Published') : [];
    setText(offerPreview, '[data-preview-note]', items.length ? 'Hover, focus, or tap the image to show the overlay.' :
        visible ? 'There are no published offers in this draft.' : 'The homepage section is hidden in this draft.');
    setText(previewHeader, '[data-section-label]', settings.subtitle);
    setText(previewHeader, '[data-section-heading]', settings.heading);
    setText(previewHeader, '[data-section-description]', settings.introduction);
    cards.classList.toggle('content-single-preview', Boolean(item));
    for (const offer of items) {
        const card = find('[data-offer-card]').content.firstElementChild.cloneNode(true);
        fillPreview(card, offer);
        setText(card, '[data-overlay-title]', offer.overlayTitle);
        find('[data-preview-modal]', card).addEventListener('click', () => {
            showDetails({ ...offer, title: offer.modalTitle, description: offer.modalDescription });
        });

        setText(card, '[data-overlay-description]', offer.overlayDescription);
        const link = find('[data-preview-link]', card);
        link.hidden = offer.buttonVisibility !== 'visible';
        if (!link.hidden) {
            link.href = offer.buttonDestination;
            find('span', link).textContent = offer.buttonText;
        }
        const visual = find('figure', card);
        visual.addEventListener('click', event => {
            if (!event.target.closest('a')) visual.focus();
        });
        cards.append(card);
    }
    offerPreview.showModal();
}

function openSectionSettings() {
    for (const name of ['subtitle', 'heading', 'introduction']) {
        sectionForm.elements[name].value = sectionSettings[name];
        error(sectionForm.elements[name]);
    }
    sectionForm.elements.visibility.checked = sectionSettings.visibility === 'Published';
    openDrawer(sectionDrawer);
}

find('[data-section-preview]').addEventListener('click', () => {
    if (validate(sectionForm)) showOffers(undefined, readSection());
});

sectionForm.addEventListener('submit', async event => {
    event.preventDefault();
    if (!validate(sectionForm)) return;
    if (!await persist({ ...state, section: readSection() }, { errorTitle: 'Could not save section settings', dialog: sectionDrawer })) return;
    sectionDrawer.close();
    notifySuccess('Section settings saved');
});

function openDeleteConfirmation(heading, description, label) {
    setText(deleteDialog, '#content-delete-heading', heading);
    setText(deleteDialog, '#content-delete-description', description);
    if (window.HoleInHubButtons) window.HoleInHubButtons.setLabel(confirmDelete, label);
    else confirmDelete.textContent = label;
    deleteDialog.showModal();
    window.HoleInHubMotion?.enter(deleteDialog, { opacity: .65 });
}

confirmDelete.addEventListener('click', async () => {
    if ('category' in deletion) {
        await deleteCategory(deletion.category);
        return;
    }
    const { key, index } = deletion;
    const rows = [...lists[key]];
    const [item] = rows.splice(index, 1);
    const itemType = key === 'gallery' ? 'Gallery item' : 'Offer';
    if (!await persist({ ...state, [key]: rows }, { errorTitle: itemType + ' could not be deleted', dialog: deleteDialog })) return;
    deleteDialog.close();
    const next = lists[key][index] ?? lists[key].at(-1);
    render(key, next, 'edit');
    if (!next) find('[data-content-list="' + key + '"] [data-action="add"]').focus();
    notifySuccess(itemType + ' deleted', item.title);
});

root.addEventListener('click', async event => {
    const close = event.target.closest('[data-close]');
    if (close) {
        close.closest('dialog').close();
        return;
    }
    const button = event.target.closest('[data-action]');
    if (!button) return;
    const { action } = button.dataset;
    const key = button.closest('[data-content-list]').dataset.contentList;
    const index = Number(button.closest('tr')?.dataset.index);
    const item = lists[key][index];
    switch (action) {
        case 'categories':
            categoryMode();
            renderCategories();
            categoryDialog.showModal();
            window.HoleInHubMotion?.enter(categoryDialog, { opacity: .65 });
            break;
        case 'add':
        case 'edit':
            openEditor(key, index, button);
            break;
        case 'section-settings':
            openSectionSettings();
            break;
        case 'preview':
            if (key === 'offers') showOffers(item);
            else showDetails(item);
            break;
        case 'delete':
            deletion = { key, index };
            openDeleteConfirmation('Delete item?', 'Delete “' + item.title + '” from the website?', 'Delete item');
            break;
        case 'visibility': {
            const visibility = item.visibility === 'Published' ? 'Hidden' : 'Published';
            const rows = lists[key].map(row => row === item ? { ...row, visibility } : row);
            if (!await persist({ ...state, [key]: rows }, { errorTitle: 'Could not change visibility' })) break;
            render(key, lists[key][index], action);
            notifySuccess(visibility === 'Published' ? 'Item published' : 'Item hidden', item.title);
            break;
        }
    }
});

const tabList = find('[role="tablist"]');
const tabs = [...tabList.querySelectorAll('[role="tab"]')];
function activateTab(activeTab) {
    if (activeTab.getAttribute('aria-selected') !== 'true') {
        for (const cancel of Object.values(cancelReorder)) cancel();
    }
    for (const tab of tabs) {
        const selected = tab === activeTab;
        tab.setAttribute('aria-selected', String(selected));
        tab.tabIndex = selected ? 0 : -1;
        document.getElementById(tab.getAttribute('aria-controls')).hidden = !selected;
    }
}

tabList.addEventListener('click', event => {
    const tab = event.target.closest('[role="tab"]');
    if (tab) activateTab(tab);
});

tabList.addEventListener('keydown', event => {
    const index = tabs.indexOf(event.target);
    if (index < 0) return;
    const destinations = {
        ArrowLeft: (index + tabs.length - 1) % tabs.length,
        ArrowRight: (index + 1) % tabs.length,
        Home: 0,
        End: tabs.length - 1
    };
    if (!Object.hasOwn(destinations, event.key)) return;
    event.preventDefault();
    const tab = tabs[destinations[event.key]];
    activateTab(tab);
    tab.focus();
});

try {
    const initial = await request();
    applyState(initial.state);
    offerCategories = initial.offerCategories;
    for (const [key, panel] of Object.entries(panels)) {
        updateCategoryFilter(key);
        mountContentReorder(key, panel);
        find('[data-content-search]', panel).addEventListener('input', () => render(key));
        find('[data-content-category]', panel).addEventListener('change', () => render(key));
        render(key);
        find('.content-toolbar', panel).inert = false;
    }
    if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        find('[data-content-list]:not([hidden]) tbody').animate([{ opacity: .76 }, { opacity: 1 }], { duration: 150, easing: 'ease-out' });
    }
} catch (error) {
    listControls.hidden = true;
    const message = error instanceof TypeError ? 'Could not reach the server.' : error.message;
    window.HoleInHubToasts?.show({ kind: 'error', title: 'Could not load website content', message: message + ' Reload to try again.' });
} finally {
    find('[data-content-loading]').remove();
    for (const panel of Object.values(panels)) {
        find('.content-table-wrap', panel).removeAttribute('aria-busy');
        panel.querySelectorAll('.content-skeleton-row').forEach(row => row.remove());
    }
}
