(function () {
    'use strict';

    var currentHits = [];
    var currentIndex = -1;

    function escapeRegex(text) {
        return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    function clearHighlights(root) {
        var marks = root.querySelectorAll('mark.help-hit');
        for (var i = 0; i < marks.length; i++) {
            var mark = marks[i];
            var parent = mark.parentNode;
            while (mark.firstChild) {
                parent.insertBefore(mark.firstChild, mark);
            }
            parent.removeChild(mark);
            parent.normalize();
        }
    }

    function collectTextNodes(root) {
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode: function (node) {
                if (!node.nodeValue || !node.nodeValue.trim()) {
                    return NodeFilter.FILTER_REJECT;
                }
                var parent = node.parentNode;
                while (parent && parent !== root) {
                    var tag = parent.nodeName;
                    if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'CODE' || tag === 'PRE') {
                        return NodeFilter.FILTER_REJECT;
                    }
                    parent = parent.parentNode;
                }
                return NodeFilter.FILTER_ACCEPT;
            }
        });
        var nodes = [];
        var node;
        while ((node = walker.nextNode())) {
            nodes.push(node);
        }
        return nodes;
    }

    function highlight(root, query) {
        clearHighlights(root);
        currentHits = [];
        currentIndex = -1;
        var trimmed = (query || '').trim();
        if (trimmed.length < 2) {
            return 0;
        }
        var pattern = new RegExp(escapeRegex(trimmed), 'gi');
        var textNodes = collectTextNodes(root);
        for (var i = 0; i < textNodes.length; i++) {
            var node = textNodes[i];
            var text = node.nodeValue;
            pattern.lastIndex = 0;
            var match;
            var lastIndex = 0;
            var fragment = document.createDocumentFragment();
            var hadMatch = false;
            while ((match = pattern.exec(text)) !== null) {
                hadMatch = true;
                if (match.index > lastIndex) {
                    fragment.appendChild(document.createTextNode(text.substring(lastIndex, match.index)));
                }
                var mark = document.createElement('mark');
                mark.className = 'help-hit';
                mark.textContent = match[0];
                fragment.appendChild(mark);
                currentHits.push(mark);
                lastIndex = pattern.lastIndex;
                if (match.index === pattern.lastIndex) {
                    pattern.lastIndex++;
                }
            }
            if (hadMatch) {
                if (lastIndex < text.length) {
                    fragment.appendChild(document.createTextNode(text.substring(lastIndex)));
                }
                node.parentNode.replaceChild(fragment, node);
            }
        }
        if (currentHits.length > 0) {
            focusHit(0);
        }
        return currentHits.length;
    }

    function focusHit(index) {
        if (currentHits.length === 0) {
            return;
        }
        if (index < 0) { index = currentHits.length - 1; }
        if (index >= currentHits.length) { index = 0; }
        for (var i = 0; i < currentHits.length; i++) {
            currentHits[i].classList.remove('current');
        }
        currentIndex = index;
        var hit = currentHits[index];
        hit.classList.add('current');
        hit.scrollIntoView({ block: 'center', behavior: 'auto' });
    }

    function scrollToSlug(slug) {
        if (!slug) { return false; }
        var target = document.getElementById(slug);
        if (!target) { return false; }
        target.scrollIntoView({ block: 'start', behavior: 'auto' });
        return true;
    }

    window.MdExplorerHelp = {
        highlight: function (query) {
            var content = document.getElementById('content');
            if (!content) { return 0; }
            return highlight(content, query);
        },
        next: function () { focusHit(currentIndex + 1); return currentIndex; },
        previous: function () { focusHit(currentIndex - 1); return currentIndex; },
        scrollToSlug: scrollToSlug,
        hitCount: function () { return currentHits.length; },
        currentIndex: function () { return currentIndex; }
    };
})();
