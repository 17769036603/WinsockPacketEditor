const fs = require("fs");
const vm = require("vm");

const source = fs.readFileSync("app/src/main/assets/jinchan_agent.js", "utf8");
const start = source.indexOf("function finishFromFile(path) {");
const end = source.indexOf("\nfunction begin(", start);
if (start < 0 || end < 0) throw new Error("finishFromFile extraction failed");

let contents = "{";
const events = [];
class JFile {
  static $new(path) { return { path, exists() { return true; } }; }
}
class Scanner {
  static $new() {
    return {
      useDelimiter() { return this; },
      hasNext() { return contents.length > 0; },
      next() { return contents; },
      close() {}
    };
  }
}
const context = vm.createContext({
  operationNonce: "nonce-1",
  done: false,
  send: event => events.push(event),
  frida_java_bridge_default: {
    use(name) { return name === "java.io.File" ? JFile : Scanner; }
  }
});
vm.runInContext(source.slice(start, end), context);

if (context.finishFromFile("result.json") !== false || context.done || events.length !== 0) {
  throw new Error("partial JSON must keep polling");
}
contents = '{"nonce":"nonce-2","ok":true,"restored":true}';
if (context.finishFromFile("result.json") !== false || context.done) {
  throw new Error("nonce mismatch must keep waiting");
}
contents = '{"nonce":"nonce-1","ok":true,"restored":true}';
if (context.finishFromFile("result.json") !== true || !context.done
    || events.length !== 1 || events[0].ok !== true) {
  throw new Error("complete matching result must finish");
}
console.log("Jinchan result polling self-test: PASS");
