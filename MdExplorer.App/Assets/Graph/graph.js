// MdExplorer-Graph-Renderer — vanilla JS Force-Directed-Layout auf <canvas>.
// Bewusst d3-frei: keine externen Skripte, keine CDN-Aufrufe.
// Algorithmus: simples Spring-Modell (Repulsion zwischen allen Knoten + Federn entlang der Kanten),
// 300 Iterationen oder bis Energy < epsilon. Zoom & Pan via Maus.
(function () {
    "use strict";

    const REPULSION_STRENGTH = 4500;
    const SPRING_LENGTH = 90;
    const SPRING_STRENGTH = 0.04;
    const DAMPING = 0.85;
    const MIN_KINETIC_ENERGY = 0.05;
    const MAX_ITERATIONS = 300;
    const NODE_RADIUS_BASE = 5;
    const NODE_RADIUS_SCALE = 4;

    function render(payload) {
        const canvas = document.getElementById("graph");
        const ctx = canvas.getContext("2d");
        const legendCount = document.getElementById("legend-count");
        const nodes = payload.nodes.map(function (n, i) {
            return {
                id: n.id,
                title: n.title,
                relativePath: n.relativePath,
                incomingLinkCount: n.incomingLinkCount,
                x: Math.cos((i / payload.nodes.length) * Math.PI * 2) * 200 + 400,
                y: Math.sin((i / payload.nodes.length) * Math.PI * 2) * 200 + 300,
                vx: 0,
                vy: 0,
            };
        });
        const nodeIndex = new Map(nodes.map(function (n) { return [n.id, n]; }));
        const edges = payload.edges.map(function (e) {
            return { source: nodeIndex.get(e.sourceId), target: nodeIndex.get(e.targetId) };
        }).filter(function (e) { return e.source && e.target; });

        legendCount.textContent = nodes.length + " Knoten / " + edges.length + " Kanten";

        const view = { scale: 1, translateX: 0, translateY: 0 };
        let dragNode = null;
        let lastPointer = null;

        function resize() {
            canvas.width = canvas.clientWidth;
            canvas.height = canvas.clientHeight;
        }
        resize();
        window.addEventListener("resize", resize);

        function nodeRadius(node) {
            return NODE_RADIUS_BASE + NODE_RADIUS_SCALE * Math.log10(node.incomingLinkCount + 1);
        }

        function simulateStep() {
            for (let i = 0; i < nodes.length; i++) {
                const a = nodes[i];
                if (a === dragNode) {
                    a.vx = 0;
                    a.vy = 0;
                    continue;
                }
                for (let j = i + 1; j < nodes.length; j++) {
                    const b = nodes[j];
                    let dx = b.x - a.x;
                    let dy = b.y - a.y;
                    let distSq = dx * dx + dy * dy;
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
            }
            for (let i = 0; i < edges.length; i++) {
                const edge = edges[i];
                const dx = edge.target.x - edge.source.x;
                const dy = edge.target.y - edge.source.y;
                const dist = Math.sqrt(dx * dx + dy * dy) || 0.001;
                const force = (dist - SPRING_LENGTH) * SPRING_STRENGTH;
                const fx = (dx / dist) * force;
                const fy = (dy / dist) * force;
                if (edge.source !== dragNode) {
                    edge.source.vx += fx;
                    edge.source.vy += fy;
                }
                if (edge.target !== dragNode) {
                    edge.target.vx -= fx;
                    edge.target.vy -= fy;
                }
            }
            let energy = 0;
            for (let i = 0; i < nodes.length; i++) {
                const n = nodes[i];
                n.vx *= DAMPING;
                n.vy *= DAMPING;
                n.x += n.vx;
                n.y += n.vy;
                energy += n.vx * n.vx + n.vy * n.vy;
            }
            return energy;
        }

        function draw() {
            ctx.fillStyle = "#1e1e1e";
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.save();
            ctx.translate(view.translateX, view.translateY);
            ctx.scale(view.scale, view.scale);

            ctx.strokeStyle = "rgba(180, 180, 200, 0.4)";
            ctx.lineWidth = 1 / view.scale;
            for (let i = 0; i < edges.length; i++) {
                const edge = edges[i];
                ctx.beginPath();
                ctx.moveTo(edge.source.x, edge.source.y);
                ctx.lineTo(edge.target.x, edge.target.y);
                ctx.stroke();
            }

            for (let i = 0; i < nodes.length; i++) {
                const n = nodes[i];
                const r = nodeRadius(n);
                ctx.beginPath();
                ctx.arc(n.x, n.y, r, 0, Math.PI * 2);
                ctx.fillStyle = "#5aa9e6";
                ctx.fill();
                ctx.strokeStyle = "#1e1e1e";
                ctx.lineWidth = 1.5 / view.scale;
                ctx.stroke();
            }

            ctx.fillStyle = "#e6e6e6";
            ctx.font = (10 / view.scale) + "px 'Segoe UI', sans-serif";
            for (let i = 0; i < nodes.length; i++) {
                const n = nodes[i];
                ctx.fillText(n.title, n.x + nodeRadius(n) + 2, n.y + 3);
            }

            ctx.restore();
        }

        let iteration = 0;
        function tick() {
            const energy = simulateStep();
            draw();
            iteration++;
            if (iteration < MAX_ITERATIONS && energy > MIN_KINETIC_ENERGY) {
                requestAnimationFrame(tick);
            }
        }
        requestAnimationFrame(tick);

        function screenToWorld(x, y) {
            return {
                x: (x - view.translateX) / view.scale,
                y: (y - view.translateY) / view.scale,
            };
        }

        canvas.addEventListener("wheel", function (event) {
            event.preventDefault();
            const factor = event.deltaY < 0 ? 1.1 : 1 / 1.1;
            const before = screenToWorld(event.offsetX, event.offsetY);
            view.scale *= factor;
            const after = screenToWorld(event.offsetX, event.offsetY);
            view.translateX += (after.x - before.x) * view.scale;
            view.translateY += (after.y - before.y) * view.scale;
            iteration = MAX_ITERATIONS;
            draw();
        }, { passive: false });

        canvas.addEventListener("pointerdown", function (event) {
            const world = screenToWorld(event.offsetX, event.offsetY);
            dragNode = null;
            for (let i = nodes.length - 1; i >= 0; i--) {
                const n = nodes[i];
                const r = nodeRadius(n);
                if ((world.x - n.x) ** 2 + (world.y - n.y) ** 2 <= r * r * 2.5) {
                    dragNode = n;
                    break;
                }
            }
            lastPointer = { x: event.offsetX, y: event.offsetY };
            canvas.setPointerCapture(event.pointerId);
        });

        canvas.addEventListener("pointermove", function (event) {
            if (lastPointer === null) {
                return;
            }
            if (dragNode !== null) {
                const world = screenToWorld(event.offsetX, event.offsetY);
                dragNode.x = world.x;
                dragNode.y = world.y;
                iteration = 0;
            } else {
                view.translateX += event.offsetX - lastPointer.x;
                view.translateY += event.offsetY - lastPointer.y;
            }
            lastPointer = { x: event.offsetX, y: event.offsetY };
            draw();
        });

        canvas.addEventListener("pointerup", function (event) {
            canvas.releasePointerCapture(event.pointerId);
            lastPointer = null;
            dragNode = null;
        });
    }

    window.MdExplorerGraph = { render: render };
})();
