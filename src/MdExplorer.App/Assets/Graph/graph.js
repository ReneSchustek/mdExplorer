// MdExplorer-Graph-Renderer: vanilla JS Force-Directed-Layout auf <canvas>.
// Bewusst d3-frei: keine externen Skripte, keine CDN-Aufrufe.
// Algorithmus: simples Spring-Modell (Repulsion zwischen allen Knoten + Federn entlang der Kanten),
// 300 Iterationen oder bis Energy < epsilon. Zoom und Verschieben mit Maus oder Tastatur.
// Die Abstoßung zwischen allen Paaren wächst mit dem Quadrat der Knotenzahl; die Grenze dafür
// setzt GraphOptions.MaxNodes (Voreinstellung 1000) schon beim Bau des Snapshots.
(function () {
    "use strict";

    // Stellgrößen der Simulation, abgestimmt auf Bildpunkte der Zeichenfläche. Abstoßung wirkt
    // mit dem Quadrat des Abstands, eine Kante zieht ihre Enden auf SPRING_LENGTH zusammen,
    // DAMPING ist der Anteil der Geschwindigkeit, der je Schritt bleibt. MAX_ITERATIONS beendet
    // die Simulation auch dann, wenn sie nie zur Ruhe kommt; bei 60 Bildern je Sekunde nach
    // etwa fünf Sekunden. Der Knotenradius wächst mit dem Logarithmus der eingehenden Verweise,
    // damit ein vielfach verlinktes Dokument nicht den ganzen Graphen überdeckt.
    const REPULSION_STRENGTH = 4500;
    const SPRING_LENGTH = 90;
    const SPRING_STRENGTH = 0.04;
    const DAMPING = 0.85;
    const MIN_KINETIC_ENERGY = 0.05;
    const MAX_ITERATIONS = 300;
    const NODE_RADIUS_BASE = 5;
    const NODE_RADIUS_SCALE = 4;
    // Tastaturbedienung: ein Zoomschritt wie eine Rastung des Mausrads, ein Pfeilschritt in
    // Bildpunkten der Zeichenfläche.
    const ZOOM_STEP = 1.1;
    const PAN_STEP = 40;

    // Die Zeichenfläche kennt kein CSS. Sie bekommt jede Farbe als Wert übergeben.
    // Deshalb werden die Variablen des Stylesheets einmal ausgelesen; so steht die
    // Belegung an einer Stelle und gilt für Fläche, Kanten, Knoten und Beschriftung.
    function readPalette() {
        const style = getComputedStyle(document.documentElement);
        function value(name, fallback) {
            const read = style.getPropertyValue(name).trim();
            return read.length > 0 ? read : fallback;
        }
        return {
            canvas: value("--graph-canvas", "#ffffff"),
            edge: value("--graph-edge", "rgba(90, 100, 120, 0.45)"),
            node: value("--graph-node", "#2f6fa8"),
            nodeOutline: value("--graph-node-outline", "#ffffff"),
            label: value("--graph-label", "#1c1c1c"),
        };
    }

    // Setzt Zahlen in einen Text aus der Anwendung ein, etwa „{0} Knoten / {1} Kanten".
    function format(template, first, second) {
        return template.replace("{0}", String(first)).replace("{1}", String(second));
    }

    // Startpunkte auf einem Kreis: Kein Paar liegt aufeinander, und die Abstoßung hat von
    // Anfang an eine Richtung.
    function createNodes(payloadNodes) {
        return payloadNodes.map(function (n, i) {
            const angle = (i / payloadNodes.length) * Math.PI * 2;
            return {
                id: n.id,
                title: n.title,
                relativePath: n.relativePath,
                incomingLinkCount: n.incomingLinkCount,
                x: Math.cos(angle) * 200 + 400,
                y: Math.sin(angle) * 200 + 300,
                vx: 0,
                vy: 0,
            };
        });
    }

    // Kanten zu Knoten, die nicht im Snapshot stehen, fallen weg.
    function createEdges(payloadEdges, nodes) {
        const nodeIndex = new Map(nodes.map(function (n) { return [n.id, n]; }));
        return payloadEdges.map(function (e) {
            return { source: nodeIndex.get(e.sourceId), target: nodeIndex.get(e.targetId) };
        }).filter(function (e) { return e.source && e.target; });
    }

    function nodeRadius(node) {
        return NODE_RADIUS_BASE + NODE_RADIUS_SCALE * Math.log10(node.incomingLinkCount + 1);
    }

    function repel(a, b) {
        let dx = b.x - a.x;
        let dy = b.y - a.y;
        let distSq = dx * dx + dy * dy;
        // Zwei Knoten auf demselben Punkt haben keine Richtung zueinander; ein kleiner
        // Zufallsstoß trennt sie, statt durch null zu teilen.
        if (distSq < 0.01) {
            dx = Math.random() - 0.5;
            dy = Math.random() - 0.5;
            distSq = dx * dx + dy * dy;
        }
        const force = REPULSION_STRENGTH / distSq;
        const dist = Math.sqrt(distSq);
        const fx = (dx / dist) * force;
        const fy = (dy / dist) * force;
        a.vx -= fx;
        a.vy -= fy;
        b.vx += fx;
        b.vy += fy;
    }

    function applyRepulsion(graph) {
        const nodes = graph.nodes;
        for (let i = 0; i < nodes.length; i++) {
            if (nodes[i] === graph.dragNode) {
                nodes[i].vx = 0;
                nodes[i].vy = 0;
                continue;
            }
            for (let j = i + 1; j < nodes.length; j++) {
                repel(nodes[i], nodes[j]);
            }
        }
    }

    function applySprings(graph) {
        for (let i = 0; i < graph.edges.length; i++) {
            const edge = graph.edges[i];
            const dx = edge.target.x - edge.source.x;
            const dy = edge.target.y - edge.source.y;
            const dist = Math.sqrt(dx * dx + dy * dy) || 0.001;
            const force = (dist - SPRING_LENGTH) * SPRING_STRENGTH;
            const fx = (dx / dist) * force;
            const fy = (dy / dist) * force;
            if (edge.source !== graph.dragNode) {
                edge.source.vx += fx;
                edge.source.vy += fy;
            }
            if (edge.target !== graph.dragNode) {
                edge.target.vx -= fx;
                edge.target.vy -= fy;
            }
        }
    }

    // Bewegt alle Knoten um ihre gedämpfte Geschwindigkeit und liefert die verbliebene Energie.
    function moveNodes(graph) {
        let energy = 0;
        for (let i = 0; i < graph.nodes.length; i++) {
            const n = graph.nodes[i];
            n.vx *= DAMPING;
            n.vy *= DAMPING;
            n.x += n.vx;
            n.y += n.vy;
            energy += n.vx * n.vx + n.vy * n.vy;
        }
        return energy;
    }

    function simulateStep(graph) {
        applyRepulsion(graph);
        applySprings(graph);
        return moveNodes(graph);
    }

    function drawEdges(graph) {
        const ctx = graph.ctx;
        ctx.strokeStyle = graph.palette.edge;
        ctx.lineWidth = 1 / graph.view.scale;
        for (let i = 0; i < graph.edges.length; i++) {
            const edge = graph.edges[i];
            ctx.beginPath();
            ctx.moveTo(edge.source.x, edge.source.y);
            ctx.lineTo(edge.target.x, edge.target.y);
            ctx.stroke();
        }
    }

    function drawNodes(graph) {
        const ctx = graph.ctx;
        for (let i = 0; i < graph.nodes.length; i++) {
            const n = graph.nodes[i];
            ctx.beginPath();
            ctx.arc(n.x, n.y, nodeRadius(n), 0, Math.PI * 2);
            ctx.fillStyle = graph.palette.node;
            ctx.fill();
            ctx.strokeStyle = graph.palette.nodeOutline;
            ctx.lineWidth = 1.5 / graph.view.scale;
            ctx.stroke();
        }
        ctx.fillStyle = graph.palette.label;
        ctx.font = (10 / graph.view.scale) + "px 'Segoe UI', sans-serif";
        for (let i = 0; i < graph.nodes.length; i++) {
            const n = graph.nodes[i];
            ctx.fillText(n.title, n.x + nodeRadius(n) + 2, n.y + 3);
        }
    }

    function draw(graph) {
        const ctx = graph.ctx;
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.fillStyle = graph.palette.canvas;
        ctx.fillRect(0, 0, graph.canvas.width, graph.canvas.height);
        ctx.save();
        ctx.scale(graph.pixelRatio, graph.pixelRatio);
        ctx.translate(graph.view.translateX, graph.view.translateY);
        ctx.scale(graph.view.scale, graph.view.scale);
        drawEdges(graph);
        drawNodes(graph);
        ctx.restore();
    }

    // Die Zeichenfläche rechnet in Bildpunkten des Bildschirms, das Layout in CSS-Pixeln. Bei
    // 150 % Skalierung wäre das Bild ohne den Faktor unscharf hochgezogen.
    function resize(graph) {
        graph.pixelRatio = window.devicePixelRatio || 1;
        graph.canvas.width = Math.round(graph.canvas.clientWidth * graph.pixelRatio);
        graph.canvas.height = Math.round(graph.canvas.clientHeight * graph.pixelRatio);
        // Das Setzen der Größe leert die Fläche. Solange die Simulation läuft, füllt sie
        // der nächste Durchlauf wieder, danach nicht mehr: Wer das Fenster später
        // vergrößert, sähe ohne diesen Aufruf einen leeren Graphen. Deshalb hier neu zeichnen.
        draw(graph);
    }

    // Die Schleife plant sich selbst Frame für Frame ein und hört auf, sobald das Bild zur
    // Ruhe kommt. Wer sie wieder braucht, startet sie über startSimulation; ein bloßes
    // Zurücksetzen des Zählers weckte eine beendete Schleife nicht mehr auf.
    function startSimulation(graph) {
        graph.iteration = 0;
        if (graph.running) {
            return;
        }
        graph.running = true;
        function tick() {
            const energy = simulateStep(graph);
            draw(graph);
            graph.iteration++;
            if (graph.iteration < MAX_ITERATIONS && energy > MIN_KINETIC_ENERGY) {
                requestAnimationFrame(tick);
            } else {
                graph.running = false;
            }
        }
        requestAnimationFrame(tick);
    }

    function screenToWorld(graph, x, y) {
        return {
            x: (x - graph.view.translateX) / graph.view.scale,
            y: (y - graph.view.translateY) / graph.view.scale,
        };
    }

    // Zoomt um den Punkt (x, y), der dabei an seiner Stelle bleibt. Zoomen beendet die
    // Simulation; das Bild steht danach still.
    function zoomAt(graph, factor, x, y) {
        const before = screenToWorld(graph, x, y);
        graph.view.scale *= factor;
        const after = screenToWorld(graph, x, y);
        graph.view.translateX += (after.x - before.x) * graph.view.scale;
        graph.view.translateY += (after.y - before.y) * graph.view.scale;
        graph.iteration = MAX_ITERATIONS;
        draw(graph);
    }

    function attachWheel(graph) {
        graph.canvas.addEventListener("wheel", function (event) {
            event.preventDefault();
            zoomAt(graph, event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, event.offsetX, event.offsetY);
        }, { passive: false });
    }

    // Dieselbe Bedienung ohne Maus. Tab verlässt die Zeichenfläche wie jedes andere Element;
    // nur die hier genannten Tasten bleiben in ihr.
    function attachKeyboard(graph) {
        const pans = {
            ArrowLeft: [PAN_STEP, 0],
            ArrowRight: [-PAN_STEP, 0],
            ArrowUp: [0, PAN_STEP],
            ArrowDown: [0, -PAN_STEP],
        };
        graph.canvas.addEventListener("keydown", function (event) {
            const centerX = graph.canvas.clientWidth / 2;
            const centerY = graph.canvas.clientHeight / 2;
            if (event.key === "+" || event.key === "=") {
                zoomAt(graph, ZOOM_STEP, centerX, centerY);
            } else if (event.key === "-") {
                zoomAt(graph, 1 / ZOOM_STEP, centerX, centerY);
            } else if (event.key === "0") {
                graph.view = { scale: 1, translateX: 0, translateY: 0 };
                draw(graph);
            } else if (pans[event.key]) {
                graph.view.translateX += pans[event.key][0];
                graph.view.translateY += pans[event.key][1];
                draw(graph);
            } else {
                return;
            }
            event.preventDefault();
        });
    }

    // Die Trefferfläche ist 2,5-mal so groß wie der Kreis (gut 1,5-facher Radius), damit auch
    // kleine Knoten zu greifen sind.
    function nodeAt(graph, x, y) {
        const world = screenToWorld(graph, x, y);
        for (let i = graph.nodes.length - 1; i >= 0; i--) {
            const n = graph.nodes[i];
            const r = nodeRadius(n);
            if ((world.x - n.x) ** 2 + (world.y - n.y) ** 2 <= r * r * 2.5) {
                return n;
            }
        }
        return null;
    }

    // Mit gegriffenem Knoten zieht die Bewegung ihn, sonst verschiebt sie die Ansicht.
    function dragOrPan(graph, event, lastPointer) {
        if (graph.dragNode === null) {
            graph.view.translateX += event.offsetX - lastPointer.x;
            graph.view.translateY += event.offsetY - lastPointer.y;
            return;
        }
        const world = screenToWorld(graph, event.offsetX, event.offsetY);
        graph.dragNode.x = world.x;
        graph.dragNode.y = world.y;
        // Die Nachbarn sollen dem gezogenen Knoten folgen, auch wenn das Bild schon stand.
        startSimulation(graph);
    }

    function attachPointer(graph) {
        const canvas = graph.canvas;
        let lastPointer = null;
        canvas.addEventListener("pointerdown", function (event) {
            graph.dragNode = nodeAt(graph, event.offsetX, event.offsetY);
            lastPointer = { x: event.offsetX, y: event.offsetY };
            canvas.setPointerCapture(event.pointerId);
        });
        canvas.addEventListener("pointermove", function (event) {
            if (lastPointer === null) {
                return;
            }
            dragOrPan(graph, event, lastPointer);
            lastPointer = { x: event.offsetX, y: event.offsetY };
            draw(graph);
        });
        canvas.addEventListener("pointerup", function (event) {
            canvas.releasePointerCapture(event.pointerId);
            lastPointer = null;
            graph.dragNode = null;
        });
    }

    function render(payload, texts) {
        const canvas = document.getElementById("graph");
        const nodes = createNodes(payload.nodes);
        const graph = {
            canvas: canvas,
            ctx: canvas.getContext("2d"),
            nodes: nodes,
            edges: createEdges(payload.edges, nodes),
            palette: readPalette(),
            view: { scale: 1, translateX: 0, translateY: 0 },
            pixelRatio: window.devicePixelRatio || 1,
            dragNode: null,
            iteration: 0,
            running: false,
        };

        document.getElementById("legend-count").textContent = format(texts.legend, graph.nodes.length, graph.edges.length);
        canvas.setAttribute("aria-label", format(texts.canvasName, graph.nodes.length, graph.edges.length));

        resize(graph);
        window.addEventListener("resize", function () { resize(graph); });
        attachWheel(graph);
        attachKeyboard(graph);
        attachPointer(graph);
        startSimulation(graph);
    }

    window.MdExplorerGraph = { render: render };
})();
