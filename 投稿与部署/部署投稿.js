#!/usr/bin/env node
/* ============================================================================
 * 部署投稿.js —— EXPAS 投稿一键导入
 *
 * 【两套东西, 完全分开】
 *   A) 自定义 UI  →  layouts\<Steam64>.txt      (HUD 排版)
 *   B) 第一名音乐盒 →  mvp_music\<Steam64>.js + 音频文件 (同一目录)
 *
 * 用法:
 *     node 部署投稿.js            正常导入(会写文件)
 *     node 部署投稿.js --dry      只检查不写入(先跑这个看有没有错)
 *
 * 收件夹: 见下方 SRC, 默认 C:\Users\35762\Desktop\EXPAS投稿
 * 可用环境变量覆盖:  EXPAS_SRC、EXPAS_CFG、EXPAS_PORT
 *
 * 它做四件事:
 *   ① 读收件夹里的 *.txt / *.js, 取出 owner(Steam64) / name / 排版行 / music
 *   ② 有排版行 → 写出  EXPAS\layouts\<Steam64>.txt        (UTF-8 无 BOM)
 *   ③ 有 music:
 *        · 写的是文件名 → 把音频复制到 EXPAS\mvp_music\, 再写 EXPAS\mvp_music\<Steam64>.js
 *        · 写的是绝对路径 → 不复制, 直接把该路径写进 EXPAS\mvp_music\<Steam64>.js
 *   ④ 打印汇总
 *
 * 写完后重启服务器生效。**不改 config.yml**(音乐盒已改为 JS 文件驱动)。
 * ========================================================================== */
'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');

const SRC = process.env.EXPAS_SRC || 'C:\\Users\\35762\\Desktop\\EXPAS投稿';
const PORT = process.env.EXPAS_PORT || '7777';
const DST = process.env.EXPAS_CFG || path.join(
    os.homedir(), 'AppData', 'Roaming', 'SCP Secret Laboratory',
    'LabAPI', 'configs', PORT, 'EXPAS');
const DRY = process.argv.includes('--dry');

const LAYOUT_DIR = path.join(DST, 'layouts');
const MUSIC_DIR = path.join(DST, 'mvp_music');
const AUDIO_DIR = path.join(DST, 'audio');

const KIND_OK = new Set(['row', 'level', 'kill', 'fold', 'custom']);
const ALIGN_OK = new Set(['left', 'center', 'right', 'l', 'c', 'r', '左', '中', '居中', '右']);

/** 插件支持解码的音频扩展名(与 EXPAS 的 AudioExtensions 对齐) */
const AUDIO_EXT_OK = new Set(['.wav', '.ogg', '.mp3', '.m4a', '.aac', '.mp4', '.flac',
    '.wma', '.opus', '.aiff', '.aif', '.wv', '.ape', '.mka', '.webm']);

const say = (s) => console.log(s);
const stripBom = (s) => s.replace(/^\uFEFF/, '');
const find17 = (s) => { const m = String(s || '').match(/\d{17}/); return m ? m[0] : null; };
const canon = (id) => (/@/.test(id) ? id : id + '@steam');

/** 把投稿拆成"纯文本行"(JS 只取引号串并剥壳; TXT 保留 # owner) */
function toLines(raw, isJs) {
    const out = [];
    for (let line of stripBom(raw).split(/\r?\n/)) {
        let t = line.trim();
        if (!t) continue;

        if (isJs) {
            if (t.startsWith('//')) {
                if (!/owner/i.test(t)) continue;
                t = t.slice(2).trim();
            }
            const quoted = [];
            const re = /"([^"]*)"|'([^']*)'/g;
            let m;
            while ((m = re.exec(t)) !== null) {
                const v = (m[1] !== undefined ? m[1] : m[2]).trim();
                if (v) quoted.push(v);
            }
            if (quoted.length) {
                const kv = t.match(/^\s*(?:const|let|var)\s+([A-Za-z_]\w*)\s*=/);
                if (kv && /^(name|owner|music|audio)$/i.test(kv[1])) out.push(kv[1].toLowerCase() + ' = ' + quoted[0]);
                else quoted.forEach(v => out.push(v));
                continue;
            }
            t = t.replace(/^\s*(?:const|let|var)\s+/, '').replace(/[;,]+$/, '').trim();
            if (/^(name|owner|music|audio)\s*=/i.test(t)) out.push(t);
            continue;
        }

        if (t.startsWith('#')) {
            if (/owner/i.test(t)) out.push(t.replace(/^#+\s*/, ''));
            continue;
        }
        out.push(t);
    }
    return out;
}

const ownerOf = (line) => (/owner/i.test(line) ? find17(line) : null);

function processOne(file) {
    const name = path.basename(file);
    const isJs = /\.js$/i.test(file);
    const lines = toLines(fs.readFileSync(file, 'utf8'), isJs);

    let owner = find17(name);
    let ownerFrom = owner ? '文件名' : '';
    if (!owner) {
        for (const l of lines) {
            const o = ownerOf(l);
            if (o) { owner = o; ownerFrom = '首行 owner'; break; }
        }
    }
    if (!owner) return { name, bad: ['缺少 Steam64(文件名开头 17 位数字, 或写 owner = <17位数字>)'] };

    const rows = [], bad = [];
    let display = null, music = null;

    for (const l of lines) {
        if (ownerOf(l)) continue;

        const kv = l.match(/^([A-Za-z_]\w*)\s*=\s*(.+)$/);
        if (kv && l.indexOf('|') < 0) {
            const k = kv[1].toLowerCase(), v = kv[2].trim();
            if (k === 'name') display = v;
            else if (k === 'music' || k === 'audio') music = v;
            continue;
        }

        const p = l.split('|');
        if (p.length < 2) { bad.push('不认识的行: ' + l); continue; }
        const kind = p[0].trim().toLowerCase();
        if (!KIND_OK.has(kind)) { bad.push('类型只能是 row/level/kill/fold/custom: ' + l); continue; }

        const align = (p[1] || 'center').trim().toLowerCase();
        if (!ALIGN_OK.has(align)) { bad.push('对齐只能是 left/center/right: ' + l); continue; }

        const off = (p[2] !== undefined ? p[2] : '0').trim();
        if (isNaN(Number(off))) { bad.push('偏移必须是数字: ' + l); continue; }

        const text = p.length > 3 ? p.slice(3).join('|') : '';
        if (kind !== 'custom' && !text.trim()) { bad.push('这一行没有文字: ' + l); continue; }
        rows.push(`${kind}|${align}|${off}|${text}`);
    }

    return { name, owner, ownerFrom, display, music, rows, bad, srcDir: path.dirname(file) };
}

/** 写一份音乐盒 JS(声明 owner + audio) */
function writeMusicJs(owner, audioValue) {
    const js = [
        '// EXPAS 第一名音乐盒 —— 回合结束伤害第一名时播放',
        '// 文件名 = 归属 Steam64; audio = 音频(相对 mvp_music\\, 也可写绝对路径)',
        '',
        `const owner = "${canon(owner)}";`,
        `const audio = "${audioValue}";`,
        ''
    ].join('\r\n');
    if (!DRY) fs.writeFileSync(path.join(MUSIC_DIR, owner + '.js'), js, 'utf8');
}

(function main() {
    say('=== EXPAS 投稿导入' + (DRY ? '(试运行, 不写文件)' : '') + ' ===');
    say('收件夹: ' + SRC);
    say('目标  : ' + DST);

    if (!fs.existsSync(SRC)) fs.mkdirSync(SRC, { recursive: true });
    if (!DRY) {
        fs.mkdirSync(LAYOUT_DIR, { recursive: true });
        fs.mkdirSync(MUSIC_DIR, { recursive: true });
        fs.mkdirSync(AUDIO_DIR, { recursive: true });
    }

    const files = fs.readdirSync(SRC)
        .filter(f => /\.(txt|js)$/i.test(f))
        .filter(f => !/^部署投稿\.js$/i.test(f));

    if (!files.length) { say('\n收件夹里没有 .txt / .js 投稿。把投稿放进去再跑一次。'); return; }

    let ok = 0;
    const failed = [];

    for (const f of files) {
        const r = processOne(path.join(SRC, f));
        if (r.bad && r.bad.length && !r.owner) { failed.push([f, r.bad]); continue; }
        if (!r.rows.length && !r.music && !(r.bad && r.bad.length)) {
            failed.push([f, ['没有任何有效内容(既没有排版行, 也没有 music)']]); continue;
        }

        const notes = [];

        // A) 自定义 UI → layouts\<Steam64>.txt
        if (r.rows.length) {
            const body = [];
            if (r.display) body.push('name = ' + r.display);
            body.push(...r.rows);
            if (!DRY) {
                const head = [
                    '# EXPAS 投稿自动导入 —— owner = ' + r.owner,
                    '# 来源: ' + f + '   时间: ' + new Date().toISOString()
                ];
                fs.writeFileSync(path.join(LAYOUT_DIR, r.owner + '.txt'), head.concat(body).join('\r\n') + '\r\n', 'utf8');
            }
            notes.push(`UI → layouts\\${r.owner}.txt  (${r.rows.length} 行)`);
        }

        // B) 第一名音乐盒 → mvp_music\<Steam64>.js (+ 音频)
        if (r.music) {
            if (path.isAbsolute(r.music)) {
                const exists = fs.existsSync(r.music);
                writeMusicJs(r.owner, r.music);
                notes.push(`音乐盒 → mvp_music\\${r.owner}.js  →  路径 ${r.music}${exists ? '' : '   [警告: 该路径当前不存在]'}`);
                if (!exists) (r.bad = r.bad || []).push('music 指定的路径不存在: ' + r.music);
            } else {
                const from = path.join(r.srcDir, r.music);
                // 保留原扩展名(不再一律改名 .wav —— 插件现在支持 OGG/MP3/AAC/M4A/MP4/FLAC/WMA…)
                const ext = (path.extname(r.music) || '.wav').toLowerCase();
                const dstName = r.owner + ext;
                if (!AUDIO_EXT_OK.has(ext))
                    (r.bad = r.bad || []).push('音频扩展名 ' + ext + ' 不在推荐列表(可能无法解码): ' + r.music);
                if (fs.existsSync(from)) {
                    if (!DRY) fs.copyFileSync(from, path.join(MUSIC_DIR, dstName));
                    writeMusicJs(r.owner, dstName);
                    notes.push(`音乐盒 → mvp_music\\${dstName} + ${r.owner}.js`);
                } else {
                    (r.bad = r.bad || []).push('music 指定的音频找不到(投稿同目录): ' + r.music);
                    notes.push('音乐盒: 音频缺失, 已跳过');
                }
            }
        }

        say(`\nOK  ${f}`);
        say('    owner = ' + r.owner + '  (来自' + r.ownerFrom + ')');
        notes.forEach(n => say('    ' + n));
        if (r.bad && r.bad.length) {
            say('    跳过/警告 ' + r.bad.length + ' 条:');
            r.bad.forEach(b => say('      - ' + b));
        }
        ok++;
    }

    say('\n---------------- 汇总 ----------------');
    say(`成功 ${ok} 份` + (failed.length ? `, 失败 ${failed.length} 份` : ''));
    failed.forEach(([f, errs]) => {
        say('  ✗ ' + f);
        errs.forEach(e => say('    - ' + e));
    });
    say('\n下一步: 重启服务器生效。');
    say('自检:  EXPAS layout list      (自定义 UI)');
    say('       EXPAS mvpmusic list    (音乐盒: 会显示每条 JS 与"文件存在/缺失")');
})();
