// ZIP with stored (uncompressed) entries: PDFs are already compressed images.
const encoder = new TextEncoder();
const table = Uint32Array.from({ length: 256 }, (_, n) => {
  for (let i = 0; i < 8; i++) n = n & 1 ? 0xedb88320 ^ (n >>> 1) : n >>> 1;
  return n >>> 0;
});
function crc32(data) {
  let value = -1;
  for (const byte of data) value = table[(value ^ byte) & 255] ^ (value >>> 8);
  return (value ^ -1) >>> 0;
}
function header(size) { return new DataView(new ArrayBuffer(size)); }
export async function zipFiles(files) {
  if (files.length > 65535) throw new Error("מספר הקבצים גדול מדי ל־ZIP.");
  const local = [], central = [];
  let offset = 0, centralSize = 0;
  for (const { name, blob } of files) {
    const filename = encoder.encode(name), data = new Uint8Array(await blob.arrayBuffer());
    const crc = crc32(data), length = data.byteLength;
    if (length > 0xffffffff || offset + length > 0xffffffff) throw new Error("הקובץ גדול מדי ל־ZIP.");
    const record = header(30 + filename.length);
    record.setUint32(0, 0x04034b50, true); record.setUint16(4, 20, true);
    record.setUint16(6, 0x0800, true); record.setUint32(14, crc, true);
    record.setUint32(18, length, true); record.setUint32(22, length, true);
    record.setUint16(26, filename.length, true);
    new Uint8Array(record.buffer, 30).set(filename);
    local.push(record.buffer, data);
    const directory = header(46 + filename.length);
    directory.setUint32(0, 0x02014b50, true); directory.setUint16(4, 20, true);
    directory.setUint16(6, 20, true); directory.setUint16(8, 0x0800, true);
    directory.setUint32(16, crc, true); directory.setUint32(20, length, true);
    directory.setUint32(24, length, true); directory.setUint16(28, filename.length, true);
    directory.setUint32(42, offset, true);
    new Uint8Array(directory.buffer, 46).set(filename);
    central.push(directory.buffer); centralSize += directory.byteLength;
    offset += record.byteLength + length;
  }
  const end = header(22);
  end.setUint32(0, 0x06054b50, true);
  end.setUint16(8, files.length, true); end.setUint16(10, files.length, true);
  end.setUint32(12, centralSize, true); end.setUint32(16, offset, true);
  return new Blob([...local, ...central, end.buffer], { type: "application/zip" });
}
