(() => {
    const controls = new WeakMap(); let opened, sequence=0;
    function enhance(select) {
        if (controls.has(select) || select.multiple || select.size>1 || select.hasAttribute('data-native-select')) return;
        const wrapper=document.createElement('div'); wrapper.className='app-select'; wrapper.dataset.open='false';
        const trigger=document.createElement('button');trigger.type='button';trigger.className='app-select-trigger';
        const text=document.createElement('span'), caret=document.createElement('span');caret.className='app-select-caret';caret.setAttribute('aria-hidden','true');trigger.append(text,caret);
        const menu=document.createElement('div');menu.className='app-select-menu';if(typeof menu.showPopover==='function')menu.setAttribute('popover','manual');menu.id='app-select-'+(++sequence);menu.setAttribute('role','listbox');
        trigger.setAttribute('aria-haspopup','listbox');trigger.setAttribute('aria-controls',menu.id);trigger.setAttribute('aria-expanded','false');
        const label=select.getAttribute('aria-label') || select.labels?.[0]?.textContent.trim() || select.name || 'Choose an option';trigger.setAttribute('aria-label',label);menu.setAttribute('aria-label',label);
        const nativeTabIndex=select.tabIndex;let options=[],index=0,typeahead='',typeTimer;
        function sync() {
            options=[...select.options];index=Math.max(0,select.selectedIndex);
            text.textContent=options[index]?.textContent || 'Choose an option';trigger.disabled=select.disabled;trigger.tabIndex=nativeTabIndex;
            trigger.setAttribute('aria-required',String(select.required));wrapper.hidden=select.hidden;
            [...menu.children].forEach((button,i)=>{button.setAttribute('aria-selected',String(i===select.selectedIndex));});
        }
        function close(focus=false) {setTimeout(()=>{if(wrapper.dataset.open==='false')menu.hidePopover?.();},window.matchMedia('(prefers-reduced-motion: reduce)').matches?0:180);wrapper.dataset.open='false';trigger.setAttribute('aria-expanded','false');if(opened?.select===select)opened=null;if(focus)trigger.focus();}
        function position() {
            const bounds=trigger.getBoundingClientRect(),spaceBelow=innerHeight-bounds.bottom-12;
            const height=Math.min(280,Math.max(100,spaceBelow>=160?spaceBelow:bounds.top-12));
            menu.style.width=Math.max(bounds.width,72)+'px';menu.style.left=Math.max(8,Math.min(bounds.left,innerWidth-Math.max(bounds.width,72)-8))+'px';
            menu.style.maxHeight=height+'px';menu.style.top=(spaceBelow>=160?bounds.bottom+4:Math.max(8,bounds.top-Math.min(menu.scrollHeight,height)-4))+'px';
        }
        function open() {if(select.disabled)return;opened?.close();sync();menu.showPopover?.();wrapper.dataset.open='true';trigger.setAttribute('aria-expanded','true');opened={select,close,position};position();menu.children[index]?.focus({preventScroll:true});}
        function choose(i) {if(!options[i] || options[i].disabled || options[i].parentElement?.disabled)return;select.selectedIndex=i;select.dispatchEvent(new Event('change',{bubbles:true}));sync();close(true);}
        function rebuild() {menu.replaceChildren(...[...select.options].map((option,i)=>{const button=document.createElement('button');button.type='button';button.tabIndex=-1;button.setAttribute('role','option');button.textContent=option.textContent;button.disabled=option.disabled || Boolean(option.parentElement?.disabled);button.addEventListener('click',()=>choose(i));return button;}));sync();}
        trigger.addEventListener('click',()=>wrapper.dataset.open==='true'?close():open());
        wrapper.addEventListener('keydown',event=>{
            if(event.key==='Escape'){event.preventDefault();close(true);return;}
            if(event.key==='Tab'){close();return;}
            if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){
                event.preventDefault();if(wrapper.dataset.open!=='true'){open();return;}
                const step=event.key==='ArrowUp'?-1:1;let next=event.key==='Home'?0:event.key==='End'?options.length-1:(index+step+options.length)%options.length;
                for(let tries=0;tries<options.length;tries++){if(!menu.children[next]?.disabled)break;next=(next+step+options.length)%options.length;}
                index=next;menu.children[index]?.focus({preventScroll:true});
            } else if(event.key==='Enter' || event.key===' '){event.preventDefault();wrapper.dataset.open==='true'?choose(index):open();}
            else if(event.key.length===1 && !event.ctrlKey && !event.metaKey){event.preventDefault();clearTimeout(typeTimer);typeahead+=event.key.toLowerCase();const match=options.findIndex((option,i)=>!menu.children[i].disabled && option.textContent.trim().toLowerCase().startsWith(typeahead));if(match>=0){if(wrapper.dataset.open!=='true')open();index=match;menu.children[index]?.focus();}typeTimer=setTimeout(()=>typeahead='',600);}
        });
        wrapper.addEventListener('focusout',event=>{if(!wrapper.contains(event.relatedTarget))close();});
        select.addEventListener('change',sync);select.addEventListener('invalid',event=>{event.preventDefault();trigger.setAttribute('aria-invalid','true');trigger.focus();});
        select.form?.addEventListener('reset',()=>requestAnimationFrame(sync));
        wrapper.append(trigger,menu);select.after(wrapper);select.classList.add('app-select-native');select.tabIndex=-1;select.setAttribute('aria-hidden','true');
        controls.set(select,{sync,rebuild});rebuild();
        new MutationObserver(rebuild).observe(select,{childList:true,subtree:true,characterData:true,attributes:true,attributeFilter:['disabled','selected','label','hidden','required']});
    }
    function mount(root=document) {if(root.matches?.('select'))enhance(root);root.querySelectorAll?.('select').forEach(enhance);}
    mount();
    new MutationObserver(records=>{for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1)mount(node);}).observe(document.body,{childList:true,subtree:true});
    document.addEventListener('pointerdown',event=>{if(opened && !event.target.closest('.app-select'))opened.close();});
    document.addEventListener('focusin',event=>{if(event.target.tagName==='SELECT' && controls.has(event.target))event.target.nextElementSibling?.querySelector('button')?.focus();});
    window.addEventListener('resize',()=>opened?.position());document.addEventListener('scroll',()=>opened?.position(),true);
    window.HoleInHubSelects={mount};
})();
