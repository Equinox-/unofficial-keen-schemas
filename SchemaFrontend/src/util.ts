export function locationParameters(): { [key: string]: string[] } {
    const hash = window.location.hash;
    const result: { [key: string]: string[] } = {};
    if (hash != null && hash.startsWith('#')) {
        for (const kv of hash.substring(1).split('&')) {
            const tokens = kv.split('=', 2);
            if (tokens.length == 2) {
                const key = decodeURIComponent(tokens[0]);
                const val = decodeURIComponent(tokens[1]);
                let values = result[key];
                if (values == null) {
                    result[key] = [val];
                } else {
                    values.push(val);
                }
            }
        }
    }
    return result;
}

export function hashToSetLocationParameters(newKey: string, newValues: string[]) {
    let hash = '#';
    const params = locationParameters();
    params[newKey] = newValues;
    for (let [key, values] of Object.entries(params)) {
        if (values.length == 0) continue;
        for (const value of values) {
            if (hash.length > 1) hash += '&';
            hash += encodeURIComponent(key) + '=' + encodeURIComponent(value);
        }
    }
    return hash;
}

export function setLocationParameter(newKey: string, newValues: string[]) {
    const hash = hashToSetLocationParameters(newKey, newValues);
    if (window.location.hash == hash) return;
    window.location.hash = hash;
}