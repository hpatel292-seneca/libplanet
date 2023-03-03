import { join } from "node:path";
import { encode } from "bencodex";
import { execa } from "execa";
import { describe, expect, test } from "vitest";
import { FOO, account1, address1, address2 } from "./fixtures";
import {
  type UnsignedTxWithSystemAction,
  type UnsignedTxWithCustomActions,
} from "../../src/tx/unsigned";
import { encodeSignedTx, signTx } from "../../src/tx/signed";

describe("signTx", () => {
  test("UnsignedTxWithSystemAction", async () => {
    const unsigned: UnsignedTxWithSystemAction = {
      nonce: 123n,
      publicKey: account1.publicKey.toBytes("uncompressed"),
      signer: address1.toBytes(),
      timestamp: new Date("2022-05-23T01:02:00+00:00"),
      updatedAddresses: new Set(),
      genesisHash: null,
      systemAction: {
        type: "transfer",
        recipient: address2.toBytes(),
        amount: {
          rawValue: 12500n,
          currency: FOO,
        },
      },
    };
    const signed = await signTx(unsigned, account1);
    const encoded = await encodeSignedTx(signed);
    const payload = encode(encoded);
    const { stdout } = await execa(
      "dotnet",
      [
        "run",
        "--project",
        join(__dirname, "..", "..", "..", "..", "Libplanet.Tools"),
        "--",
        "tx",
        "analyze",
        "-",
      ],
      { input: payload },
    );
    expect(JSON.parse(stdout)).toStrictEqual({
      id: "c5defb2d96bac04cebf8c3c9e7a0dbf22fe44b06affed9abda4e4e81fc09616b",
      nonce: 123,
      signer: "268344BA46e6CA2A8a5096565548b9018bc687Ce",
      updatedAddresses: [],
      signature: Buffer.from(signed.signature.toBytes()).toString("base64"),
      systemAction: {
        "\ufefftype_id": "1",
        "\ufeffvalues": {
          "\ufeffamount": "12500",
          "\ufeffcurrency": {
            "\ufeffdecimals": "2",
            "\ufeffminters": null,
            "\ufeffticker": "\ufeffFOO",
            "\ufefftotalSupplyTrackable": true,
          },
          "\ufeffrecipient": "0x8a29de186b85560d708451101c4bf02d63b25c50",
        },
      },
      timestamp: "2022-05-23T01:02:00+00:00",
      publicKey:
        "0200e02709cc0c051dc105188c454a2e7ef7b36b85da34529d3abc1968167cf54f",
      genesisHash: null,
    });
  }, 30_000);

  test("UnsignedTxWithCustomActions", async () => {
    const unsigned: UnsignedTxWithCustomActions = {
      nonce: 123n,
      publicKey: account1.publicKey.toBytes("uncompressed"),
      signer: address1.toBytes(),
      timestamp: new Date("2022-05-23T01:02:00+00:00"),
      updatedAddresses: new Set(),
      genesisHash: null,
      customActions: [
        {
          type_id: "transfer_asset",
          values: {
            amount: [
              {
                decimalPlaces: Buffer.from([0x02]),
                minters: [
                  Buffer.from(
                    "47d082a115c63e7b58b1532d20e631538eafadde",
                    "hex",
                  ),
                ],
                ticker: "NCG",
              },
              1000,
            ],
            recipient: Buffer.from(
              "5a533067D0cBa77490268b26195EdB10B990143D",
              "hex",
            ),
            sender: Buffer.from(
              "111CB8E18c6D70f5032000c5739c5ac36E793EDB",
              "hex",
            ),
          },
        },
      ],
    };
    const signed = await signTx(unsigned, account1);
    const encoded = await encodeSignedTx(signed);
    const payload = encode(encoded);
    const { stdout } = await execa(
      "dotnet",
      [
        "run",
        "--project",
        join(__dirname, "..", "..", "..", "..", "Libplanet.Tools"),
        "--",
        "tx",
        "analyze",
        "-",
      ],
      { input: payload },
    );
    expect(JSON.parse(stdout)).toStrictEqual({
      id: "49a645bb80fa96757009615ec33bc15a2e90e9121877de9f14de35b7d657a118",
      nonce: 123,
      signer: "268344BA46e6CA2A8a5096565548b9018bc687Ce",
      updatedAddresses: [],
      signature: Buffer.from(signed.signature.toBytes()).toString("base64"),
      customActions: [
        {
          "\ufefftype_id": "\ufefftransfer_asset",
          "\ufeffvalues": {
            "\ufeffamount": [
              {
                "\ufeffdecimalPlaces": "0x02",
                "\ufeffminters": ["0x47d082a115c63e7b58b1532d20e631538eafadde"],
                "\ufeffticker": "\ufeffNCG",
              },
              "1000",
            ],
            "\ufeffsender": "0x111cb8e18c6d70f5032000c5739c5ac36e793edb",
            "\ufeffrecipient": "0x5a533067d0cba77490268b26195edb10b990143d",
          },
        },
      ],
      timestamp: "2022-05-23T01:02:00+00:00",
      publicKey:
        "0200e02709cc0c051dc105188c454a2e7ef7b36b85da34529d3abc1968167cf54f",
      genesisHash: null,
    });
  }, 30_000);
});
