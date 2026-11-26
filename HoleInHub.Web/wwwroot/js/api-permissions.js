for (const parent of document.querySelectorAll("[data-category]")) {
    const group = parent.closest("fieldset");
    const scopes = Array.from(group.querySelectorAll('[name="permission"]'));
    function sync() {
        const selected = scopes.filter(scope => scope.checked).length;
        parent.checked = selected === scopes.length;
        parent.indeterminate = selected > 0 && selected < scopes.length;
    }
    parent.addEventListener("change", () => {
        for (const scope of scopes) scope.checked = parent.checked;
    });
    group.addEventListener("change", sync);
    window.addEventListener("pageshow", sync);
    sync();
    parent.hidden = false;
}
