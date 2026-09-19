import { createPrivateKey, createSign, createVerify, constants } from 'node:crypto';
import { createReadStream, readFileSync, writeFileSync } from 'node:fs';

const archive = process.argv[2];
const privateKey = createPrivateKey({ key: Buffer.from(process.env.WINDOWS_RELEASE_KEY, 'base64'), type: 'pkcs8', format: 'der' });
const publicKey = readFileSync(new URL('./windows-release-public.pem', import.meta.url));
const signer = createSign('SHA256');
const verifier = createVerify('SHA256');
for await (const chunk of createReadStream(archive)) {
  signer.update(chunk);
  verifier.update(chunk);
}
const signature = signer.sign({ key: privateKey, padding: constants.RSA_PKCS1_PADDING });
if (signature.length !== 384 || !verifier.verify({ key: publicKey, padding: constants.RSA_PKCS1_PADDING }, signature)) throw new Error('release signing key does not match the committed RSA-3072 public key');
writeFileSync(archive + '.sig', signature);
console.log('signed and verified Windows release archive');
