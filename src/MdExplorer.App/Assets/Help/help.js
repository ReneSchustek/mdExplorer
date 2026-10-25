// Suche im Handbuch: markiert Treffer im Text und springt zwischen ihnen. Das Hilfefenster
// ruft die Funktionen unter window.MdExplorerHelp über ExecuteScriptAsync auf.
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
                    // Skripte und Stile sind kein Lesetext; Code und vorformatierter Text
                    // bleiben ebenfalls unmarkiert.
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
        // Erst ab zwei Zeichen: Ein einzelner Buchstabe träfe fast jedes Wort des Handbuchs.
        if (trimmed.length < 2) {
            return 0;
        }
        var pattern = new RegExp(escapeRegex(trimmed), 'gi');
        var textNodes = collectTextNodes(root);
        for (var i = 0; i < textNodes.length; i++) {
            markMatches(textNodes[i], pattern);
        }
        if (currentHits.length > 0) {
            focusHit(0);
        }
        return currentHits.length;
    }

    // Ersetzt einen Textknoten durch Text und <mark>-Elemente für jeden Treffer darin.
    function markMatches(node, pattern) {
        var text = node.nodeValue;
        pattern.lastIndex = 0;
        var match;
        var lastIndex = 0;
        var fragment = document.createDocumentFragment();
        var hadMatch = false;
        while ((match = pattern.exec(text)) !== null) {
            hadMatch = true;
            appendText(fragment, text.substring(lastIndex, match.index));
            var mark = document.createElement('mark');
            mark.className = 'help-hit';
            mark.textContent = match[0];
            fragment.appendChild(mark);
            currentHits.push(mark);
            lastIndex = pattern.lastIndex;
            // Ein leerer Treffer bewegte den Zeiger nicht; ohne den Schritt liefe die Schleife ewig.
            if (match.index === pattern.lastIndex) {
                pattern.lastIndex++;
            }
        }
        if (!hadMatch) {
            return;
        }
        appendText(fragment, text.substring(lastIndex));
        node.parentNode.replaceChild(fragment, node);
    }

    function appendText(fragment, text) {
        if (text.length > 0) {
            fragment.appendChild(document.createTextNode(text));
        }
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
