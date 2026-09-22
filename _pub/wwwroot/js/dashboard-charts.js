const charts = new Map();

// The C# option only names colours: the palette that answers depends on the
// MudBlazor theme in effect, which the server does not know when it serializes.
const TOKEN_VARS = {
  primary: '--mud-palette-primary',
  secondary: '--mud-palette-secondary',
  tertiary: '--mud-palette-tertiary',
  info: '--mud-palette-info',
  success: '--mud-palette-success',
  warning: '--mud-palette-warning',
  error: '--mud-palette-error',
};

function parseColor(value, fallback) {
  const text = (value || '').trim();
  const hex = /^#([0-9a-f]{3,8})$/i.exec(text);
  if (hex) {
    let digits = hex[1];
    if (digits.length === 3 || digits.length === 4) {
      digits = digits.split('').map((d) => d + d).join('');
    }
    return {
      r: parseInt(digits.slice(0, 2), 16),
      g: parseInt(digits.slice(2, 4), 16),
      b: parseInt(digits.slice(4, 6), 16),
      a: digits.length === 8 ? parseInt(digits.slice(6, 8), 16) / 255 : 1,
    };
  }

  const parts = /^rgba?\(([^)]+)\)$/i.exec(text);
  if (parts) {
    const numbers = parts[1].split(/[,/\s]+/).filter((part) => part.length).map(Number);
    return {
      r: numbers[0] || 0,
      g: numbers[1] || 0,
      b: numbers[2] || 0,
      a: numbers.length > 3 && !Number.isNaN(numbers[3]) ? numbers[3] : 1,
    };
  }

  return fallback;
}

function css(color, alpha) {
  const a = alpha === undefined ? color.a : alpha;
  return `rgba(${Math.round(color.r)}, ${Math.round(color.g)}, ${Math.round(color.b)}, ${a})`;
}

function mix(from, to, amount) {
  return {
    r: from.r + ((to.r - from.r) * amount),
    g: from.g + ((to.g - from.g) * amount),
    b: from.b + ((to.b - from.b) * amount),
    a: 1,
  };
}

function luminance(color) {
  return ((0.299 * color.r) + (0.587 * color.g) + (0.114 * color.b)) / 255;
}

function palette(element) {
  const source = element.closest('.mud-palette') || document.body;
  const styles = getComputedStyle(source);
  const read = (name, fallback) => parseColor(styles.getPropertyValue(name), fallback);

  const white = { r: 255, g: 255, b: 255, a: 1 };
  const surface = read('--mud-palette-surface', white);
  // Luminance, not a MudBlazor class name: this layer outlives MudBlazor internals.
  const dark = luminance(surface) < 0.5;
  const text = read('--mud-palette-text-primary', dark ? white : { r: 66, g: 66, b: 66, a: 1 });

  const tokens = { surface: css(surface) };
  for (const [name, variable] of Object.entries(TOKEN_VARS)) {
    const color = read(variable, { r: 89, g: 74, b: 226, a: 1 });
    // Material hues wash out over a dark surface; lifting them keeps the contrast.
    tokens[name] = css(dark ? mix(color, white, 0.18) : color);
  }

  tokens.muted = css(text, dark ? 0.45 : 0.38);
  tokens['shadow-strong'] = css(text, dark ? 0.55 : 0.5);

  const heat = parseColor(tokens.info, { r: 33, g: 150, b: 243, a: 1 });
  const peak = parseColor(tokens.primary, { r: 89, g: 74, b: 226, a: 1 });
  tokens['heat-low'] = css(mix(surface, heat, dark ? 0.16 : 0.1));
  tokens['heat-mid'] = css(heat);
  tokens['heat-high'] = css(peak);

  return {
    dark,
    tokens,
    surface: css(surface),
    text: css(text),
    textSoft: css(text, dark ? 0.72 : 0.68),
    textFaint: css(text, 0.38),
    line: css(text, dark ? 0.24 : 0.2),
    grid: css(text, dark ? 0.1 : 0.08),
    shadow: css(text, 0.06),
    tooltip: css(dark ? mix(surface, white, 0.06) : surface, 0.98),
    series: [
      tokens.primary,
      tokens.info,
      tokens.success,
      tokens.warning,
      tokens.error,
      tokens.secondary,
      tokens.tertiary,
    ],
  };
}

function resolveTokens(node, theme) {
  if (typeof node === 'string') {
    return node.startsWith('token:') ? (theme.tokens[node.slice(6)] || node) : node;
  }

  if (Array.isArray(node)) {
    return node.map((item) => resolveTokens(item, theme));
  }

  if (node && typeof node === 'object') {
    for (const key of Object.keys(node)) {
      node[key] = resolveTokens(node[key], theme);
    }
  }

  return node;
}

function numbers(kind, lang) {
  if (kind === 'usd') {
    return (value) => {
      const amount = Number(value) || 0;
      if (amount === 0) return '$0';
      const magnitude = Math.abs(amount);
      // The default tariff puts a real window below one cent: two decimals would read zero.
      const digits = magnitude >= 1 ? 2 : magnitude >= 0.01 ? 4 : 6;
      return `$${new Intl.NumberFormat(lang, { maximumFractionDigits: digits }).format(amount)}`;
    };
  }

  if (kind === 'ms') {
    const format = new Intl.NumberFormat(lang, { maximumFractionDigits: 0 });
    return (value) => `${format.format(Number(value) || 0)} ms`;
  }

  if (kind === 'compact') {
    const format = new Intl.NumberFormat(lang, { notation: 'compact', maximumFractionDigits: 1 });
    return (value) => format.format(Number(value) || 0);
  }

  const format = new Intl.NumberFormat(lang, { maximumFractionDigits: 0 });
  return (value) => format.format(Number(value) || 0);
}

function escapeHtml(value) {
  return String(value === undefined || value === null ? '' : value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function row(marker, label, value) {
  return `<div style="display:flex;gap:12px;align-items:center;justify-content:space-between">`
    + `<span>${marker || ''} ${escapeHtml(label)}</span>`
    + `<b>${escapeHtml(value)}</b></div>`;
}

function title(text) {
  return `<div style="font-weight:600;margin-bottom:4px">${escapeHtml(text)}</div>`;
}

function dot(color) {
  return `<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:${color}"></span>`;
}

function axisTooltip(meta, lang) {
  const titles = meta.tooltipTitles || [];
  const extras = meta.axisExtras || [];
  const formats = meta.seriesFormats || {};

  return (params) => {
    const points = Array.isArray(params) ? params : [params];
    if (!points.length) return '';

    const index = points[0].dataIndex;
    const lines = [title(titles[index] || points[0].axisValueLabel)];
    for (const point of points) {
      const format = numbers(formats[point.seriesName] || 'int', lang);
      lines.push(row(point.marker, point.seriesName, format(point.value)));
    }

    for (const extra of extras[index] || []) {
      lines.push(row('', extra.label, numbers(extra.format, lang)(extra.value)));
    }

    return lines.join('');
  };
}

function shareTooltip(lang) {
  const format = numbers('int', lang);
  const share = new Intl.NumberFormat(lang, { maximumFractionDigits: 1 });
  return (params) => title(params.name)
    + row(params.marker, format(params.value), `${share.format(params.percent)}%`);
}

function matrixTooltip(lang) {
  const format = numbers('int', lang);
  return (params) => {
    const count = Array.isArray(params.value) ? params.value[2] : params.value;
    return title(params.data && params.data.title ? params.data.title : params.name)
      + row(params.marker, params.seriesName || '', format(count));
  };
}

function eachAxis(option, key, apply) {
  const axis = option[key];
  if (!axis) return;
  const list = Array.isArray(axis) ? axis : [axis];
  list.forEach((entry, index) => apply(entry, index));
}

function themeAxis(axis, theme) {
  axis.axisLine = { ...(axis.axisLine || {}), lineStyle: { color: theme.line } };
  axis.axisTick = { ...(axis.axisTick || {}), lineStyle: { color: theme.line } };
  axis.axisLabel = { ...(axis.axisLabel || {}), color: theme.textSoft };
  axis.nameTextStyle = { ...(axis.nameTextStyle || {}), color: theme.textFaint, fontSize: 11 };
  axis.splitLine = { show: true, ...(axis.splitLine || {}), lineStyle: { color: theme.grid } };

  if (axis.splitArea && axis.splitArea.show) {
    axis.splitArea.areaStyle = { color: ['transparent', theme.grid] };
  }
}

function themeSeries(option, theme) {
  for (const series of option.series || []) {
    // An empty areaStyle is the marker C# leaves for "fade this line into the grid".
    if (series.type === 'line' && series.areaStyle && !series.areaStyle.color) {
      const color = parseColor(series.color, { r: 89, g: 74, b: 226, a: 1 });
      series.areaStyle.color = {
        type: 'linear',
        x: 0,
        y: 0,
        x2: 0,
        y2: 1,
        colorStops: [
          { offset: 0, color: css(color, theme.dark ? 0.32 : 0.24) },
          { offset: 1, color: css(color, 0) },
        ],
      };
    }

    if (series.type === 'bar') {
      series.emphasis = { focus: 'series', ...(series.emphasis || {}) };
    }

    series.label = { ...(series.label || {}), color: series.type === 'heatmap' ? theme.text : theme.textSoft };
  }
}

function applyTheme(option, theme, lang) {
  const meta = option.meta || {};

  option.backgroundColor = 'transparent';
  option.textStyle = { ...(option.textStyle || {}), color: theme.text };
  option.color = option.color || theme.series;

  if (option.title) {
    option.title.textStyle = { ...(option.title.textStyle || {}), color: theme.text };
    option.title.subtextStyle = { ...(option.title.subtextStyle || {}), color: theme.textFaint };
  }

  if (option.legend) {
    option.legend.textStyle = { ...(option.legend.textStyle || {}), color: theme.textSoft };
    option.legend.inactiveColor = theme.textFaint;
    option.legend.pageTextStyle = { color: theme.textSoft };
    option.legend.pageIconColor = theme.textSoft;
    option.legend.pageIconInactiveColor = theme.textFaint;
  }

  if (option.tooltip) {
    option.tooltip.backgroundColor = theme.tooltip;
    option.tooltip.borderColor = theme.line;
    option.tooltip.borderWidth = 1;
    option.tooltip.padding = [8, 12];
    option.tooltip.textStyle = { color: theme.text, fontSize: 12 };
    option.tooltip.extraCssText = 'box-shadow: 0 4px 16px rgba(0,0,0,0.18); border-radius: 6px;';
    option.tooltip.axisPointer = {
      ...(option.tooltip.axisPointer || {}),
      lineStyle: { color: theme.line },
      crossStyle: { color: theme.line },
      shadowStyle: { color: theme.shadow },
      label: { backgroundColor: theme.tooltip, color: theme.text, borderColor: theme.line, borderWidth: 1 },
    };

    if (meta.tooltip === 'share') {
      option.tooltip.formatter = shareTooltip(lang);
    } else if (meta.tooltip === 'matrix') {
      option.tooltip.formatter = matrixTooltip(lang);
    } else if (option.tooltip.trigger === 'axis') {
      option.tooltip.formatter = axisTooltip(meta, lang);
    }
  }

  const axisFormats = meta.axisFormats || {};
  eachAxis(option, 'xAxis', (axis, index) => {
    themeAxis(axis, theme);
    const format = axisFormats[`x${index}`];
    if (format) axis.axisLabel.formatter = numbers(format, lang);
  });
  eachAxis(option, 'yAxis', (axis, index) => {
    themeAxis(axis, theme);
    const format = axisFormats[`y${index}`];
    if (format) axis.axisLabel.formatter = numbers(format, lang);
  });

  for (const zoom of option.dataZoom || []) {
    if (zoom.type !== 'slider') continue;
    zoom.borderColor = 'transparent';
    zoom.backgroundColor = 'transparent';
    zoom.fillerColor = theme.grid;
    zoom.handleStyle = { color: theme.surface, borderColor: theme.textFaint };
    zoom.moveHandleStyle = { color: theme.line };
    zoom.dataBackground = { lineStyle: { color: theme.line }, areaStyle: { color: theme.grid } };
    zoom.selectedDataBackground = { lineStyle: { color: theme.textFaint }, areaStyle: { color: theme.grid } };
    zoom.textStyle = { color: theme.textFaint };
  }

  if (option.visualMap) {
    option.visualMap.textStyle = { color: theme.textSoft };
    option.visualMap.borderColor = 'transparent';
    option.visualMap.handleStyle = { borderColor: theme.line };
  }

  if (option.toolbox) {
    option.toolbox.iconStyle = { borderColor: theme.textFaint };
    option.toolbox.emphasis = { iconStyle: { borderColor: theme.text } };
    const save = option.toolbox.feature && option.toolbox.feature.saveAsImage;
    // Without this the exported PNG of a dark panel comes out transparent.
    if (save) save.backgroundColor = theme.surface;
  }

  themeSeries(option, theme);
  delete option.meta;
}

function zoomRange(params, points) {
  if (!points.length) return null;
  const payload = Array.isArray(params.batch) && params.batch[0] ? params.batch[0] : params;
  const start = payload.start ?? 0;
  const end = payload.end ?? 100;
  const last = points.length - 1;
  const fromIdx = Math.max(0, Math.min(last, Math.floor((start / 100) * points.length)));
  let toIdx = Math.max(0, Math.min(last, Math.ceil((end / 100) * points.length) - 1));
  if (toIdx < fromIdx) toIdx = fromIdx;
  return { fromUtc: points[fromIdx].fromUtc, toUtc: points[toIdx].toUtc };
}

function render(state) {
  const option = JSON.parse(state.raw);
  const meta = option.meta || {};
  state.timePoints = meta.timePoints || [];

  const theme = palette(state.element);
  const lang = document.documentElement.lang || undefined;
  resolveTokens(option, theme);
  applyTheme(option, theme, lang);

  state.ignoreZoom = true;
  state.chart.setOption(option, true);
  requestAnimationFrame(() => {
    state.ignoreZoom = false;
  });
}

export function bind(element, dotNet) {
  if (typeof echarts === 'undefined') {
    throw new Error('Apache ECharts is not loaded.');
  }

  const chart = echarts.init(element);
  const state = { element, chart, dotNet, ignoreZoom: true, timePoints: [], raw: '{}' };

  chart.on('click', (params) => {
    if (params.componentType !== 'series') return;
    const hit = params.data && params.data.hit;
    if (!hit) return;
    dotNet.invokeMethodAsync('OnChartHit', JSON.stringify(hit));
  });

  chart.on('datazoom', (params) => {
    if (state.ignoreZoom) return;
    const range = zoomRange(params, state.timePoints);
    if (!range) return;
    dotNet.invokeMethodAsync(
      'OnChartHit',
      JSON.stringify({ kind: 'datazoom', fromUtc: range.fromUtc, toUtc: range.toUtc })
    );
  });

  const observer = new ResizeObserver(() => chart.resize());
  observer.observe(element);
  state.observer = observer;
  charts.set(element, state);
}

export function setOption(element, optionJson) {
  const state = charts.get(element);
  if (!state) return;

  state.raw = optionJson;
  render(state);
}

// Repaints with the palette in effect now: the option itself did not change.
export function refresh(element) {
  const state = charts.get(element);
  if (!state) return;

  render(state);
}

export function dispose(element) {
  const state = charts.get(element);
  if (!state) return;
  state.observer?.disconnect();
  state.chart.dispose();
  charts.delete(element);
}
